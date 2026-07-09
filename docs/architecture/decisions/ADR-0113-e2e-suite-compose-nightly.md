# ADR-0113: E2E test suite — compose-orchestrated, rootless web container, nightly cadence

## Status

Proposed

## Context

Through the first eleven implemented slices, the DoD's gate 10 ("Visual / API
verification — observe the running system") was the *only* source of
runtime-only blocking defects that no automated test caught — with perfect
precision but a human-driven, one-shot execution (n=3 recorded findings across
those slices). Two concrete misses illustrate the gap:

1. **Post-login UI-state regression** — a lifecycle-override checkbox that
   became unreachable before an application's sunset date
   (`docs/superpowers/plans/2026-07-01-adr0073-cleanups*`). Deterministic:
   a scripted browser flow would have caught it on every run, but nothing
   scripted existed.
2. **Production-data-shape drift** — `GET /relationships` 500ing on a stray
   row carrying a `type` value no longer in the `RelationshipType` enum
   (`docs/superpowers/plans/2026-07-04-catalog-api-connectivity-edges*`).
   Every existing test — unit, architecture, integration — builds against a
   fresh seeded database with no drifted rows, so none could reach this
   class of bug; only observing the running system against *injected legacy
   data* surfaces it.

The project had no checked-in, automated browser-driven suite. Gate 10 was
carrying both jobs at once — catching deterministic UI regressions *and*
exploring for unknown-unknowns/data drift — with only the second job being
something a human pass is structurally suited for. The unbuilt backlog story
**E-01.F-02.S-03** ("End-to-end test infrastructure") was the natural home
for the first job. `docs/TESTING-STRATEGY.md`'s five-tier pyramid already
reserved a thin tier-5 E2E slot (ADR-0097) but it had never been realized.

Gate 10 itself needed a matching retarget: once deterministic flows have an
automated home, gate 10's mandate narrows to what a scripted suite still
cannot reach — drifted/legacy production data, unknown-unknowns, and
first-time visual surfaces — with "convert a gate-10 finding into an E2E
spec" as the expected close-out (mirroring the project's existing "any bug a
gate finds becomes a regression test" rule). Gate 10 does not fold into E2E;
per the project's no-folding rule (see `feedback_run_every_dod_gate_dont_fold`
in memory), the two remain separate lenses — one automated/deterministic, one
human/exploratory.

## Decision

Build a checked-in Playwright E2E suite and retarget gate 10 accordingly.

### Environment — option B: full compose stack + rootless web container

Playwright drives the **actual shipped web artifact**, not `vite preview`.
`docker-compose.yml` gains a `web` service (`build: web/Dockerfile`,
`4173:8080`, `depends_on: api, keycloak`) alongside the existing `postgres`,
`keycloak-db`, `keycloak` (`8180→8080`), `migrator`, and `api` (`8080`)
services. This exercises "does the CI-built image actually boot and serve
correctly" — a deploy-only defect class `vite preview` structurally cannot
reach, and the exact "bug the tests can't hit" bucket that motivated this
slice. The image and nginx config already existed (`web/Dockerfile`,
`web/nginx.conf`); the delta was wiring one compose service.

The web runtime base image moves from a root nginx image to
**`nginxinc/nginx-unprivileged:1.27-alpine`**: `nginx.conf` changes
`listen 80` → `listen 8080`; the Dockerfile's `EXPOSE` follows. Non-root
cannot bind ports below 1024, so `8080` is the deliberate consequence, not
an arbitrary choice. This is a hardening of the **shared production image**
(also built by CI's existing `images` job), not an E2E-only artifact — one
rootless base now serves both purposes.

No build args and no runtime config injection: the container is built with
its existing defaults (`VITE_API_BASE_URL=http://localhost:8080`,
`VITE_OIDC_AUTHORITY=http://localhost:8180/realms/kartova`), which the host
Playwright browser reaches via compose's published ports. This is
sufficient for a single-environment E2E run; it does **not** solve
per-environment URL configuration for real deployments (see Consequences).

### Auth — real UI login per test

A shared `login()` fixture (`e2e/fixtures/auth.ts`) drives the actual
Keycloak login page with `admin@orga.kartova.local` / `dev_password_12` on
every test, landing on `/`, completing the OIDC redirect, and returning to
`/` before in-SPA navigation. (The login helper starts at `/` for simplicity;
deep-link cold-loads themselves are fine — the OIDC `returnTo` round-trip that
fixed #47 predates this work.) This was chosen over
storageState/token reuse because `oidc-client-ts` persists the session in
`sessionStorage`, which Playwright's `storageState` mechanism does not
capture, and a ROPC shortcut would require client changes purely to serve
the test suite. For a 3-test suite (~1–2s/test login overhead) the cost is
acceptable; the gate-10 bugs this suite targets are post-login UI/data
concerns, so auth is plumbing, not the system under test. Login-once-reuse
is an explicit future optimization, not adopted now (see Consequences).

### Drift injection — per-test Postgres fixture

`e2e/fixtures/db.ts` connects directly to the compose Postgres instance as
the `kartova_bypass_rls` role and, for the relationship-drift test only,
inserts one relationship row carrying a `type` value the current mapper
does not recognize (still tagged with Org A's `tenant_id`, so it is
in-tenant-scope for the app), then deletes it on teardown. This keeps the
drifted row isolated to the one test that needs it — a global seed would
break the smoke and lifecycle-override journeys, which assume clean data.
No API path exists to create such a row (the enum value was removed), so a
direct database insert is the only route; a guarded test-seed HTTP endpoint
was considered and rejected as unnecessary backend attack surface purely to
serve tests.

### Relationship read-path hardening — EF global query filter

The 500 that motivated the drift journey was previously fixed only by
*purging* the offending row from data — the read path itself still threw
on any unmappable `type`, so any future enum removal or bad row would
recur it. This slice adds a single EF Core global query filter on the
`Relationship` entity:

```csharp
modelBuilder.Entity<Relationship>()
    .HasQueryFilter(r => KnownRelationshipTypes.Contains(r.Type));
```

where `KnownRelationshipTypes = Enum.GetValues<RelationshipType>()`. Unknown-
`type` rows are excluded at the SQL layer (`WHERE type IN (...)`) from
**every** read path — `ListRelationshipsForEntityHandler`,
`GraphTraversalHandler`, `GetApiSurfaceHandler` — without touching any of
those handlers individually. No existing `HasQueryFilter` was present on
this entity; tenant scoping is enforced via RLS (`SET LOCAL
app.current_tenant_id`), so there is no filter-composition conflict. A
backend integration test (`Kartova.Catalog.IntegrationTests`) proves the
same exclusion at the seam, independent of the E2E suite's top-of-stack
proof.

### Three journeys, thin per `docs/TESTING-STRATEGY.md`

- `e2e/tests/smoke.spec.ts` — login → Applications list renders (happy path,
  wiring proof).
- `e2e/tests/lifecycle-override.spec.ts` — the deterministic UI-state
  regression class (gate-10 finding #1), now a permanent regression test.
- `e2e/tests/relationship-drift.spec.ts` — the drifted-data graceful-degrade
  class (gate-10 finding #2), against the fixture above.

No more journeys were added; per the testing strategy, E2E stays a thin
"few critical journeys" layer, not a UI-coverage layer.

### CI cadence — nightly `schedule:` + `workflow_dispatch:`, in a separate `e2e.yml`, NOT per-PR

The E2E job lives in its own workflow file (not `.github/workflows/ci.yml`),
triggered by `schedule:` (nightly) and `workflow_dispatch:` (on demand) —
deliberately **not** `pull_request:`. Bringing up the full compose stack
(Postgres, Keycloak, migrator, API, web) on a shared CI runner costs minutes
and inherits Keycloak/compose saturation flake that would slow or destabilize
every ordinary PR. Keeping E2E out of the per-PR path preserves fast PR
feedback; the suite instead runs as a nightly regression net plus an
on-demand check. Consequence: E2E is not a gate-11 (CI-green-on-PR) check for
ordinary slice PRs. For **this** slice's own DoD, E2E was verified via a
manual `workflow_dispatch` run plus local `e2e/run.sh` evidence recorded in
the DoD ledger, not an automatic PR gate run.

### Gate-10 retarget

`CLAUDE.md`'s DoD gate 10 is reworded to center **exploratory + data-shape
observation** — the classes automated tests structurally cannot reach
(drifted/legacy production data, unknown-unknowns, first-time visual
surfaces) — and to explicitly route deterministic user-flow regressions to
the nightly E2E suite, with "convert the gate-10 finding into an E2E spec"
as the expected follow-up. Gate 10 remains a distinct, per-slice human/MCP
pass; it is not folded into or replaced by E2E — both lenses continue to run
(the project's standing no-folding rule for DoD gates).

### Boundary vs. ADR-0084 and ADR-0097

- **ADR-0084** governs the Playwright **MCP** — interactive, dev-time
  verification used during gate 10 and frontend implementation work. This
  ADR governs a *different* Playwright surface: a checked-in
  `@playwright/test` project (`e2e/`), run unattended on a schedule. The two
  are easily conflated by name; this ADR exists partly to state the
  boundary explicitly. ADR-0084 is unchanged; gate 10 (which ADR-0084
  informs) is retargeted by this ADR, not superseded.
- **ADR-0097** established the five-tier testing pyramid (architecture, unit,
  integration, contract, E2E) and reserved a thin tier-5 E2E slot. This ADR
  is the first slice to realize that tier in the repository — it does not
  change the pyramid, it fills in the previously-unbuilt layer.

## Consequences

**Positive**

- Both classes of gate-10-only defect (deterministic UI regression,
  production-data-shape drift) now have permanent, automated regression
  coverage that runs unattended.
- The rootless web image change hardens the shared production artifact
  (also built by CI's `images` job), not just the E2E environment.
- Gate 10 has a narrower, more honest mandate — it is no longer expected to
  catch deterministic regressions a machine can catch more reliably and
  cheaply every night.
- The relationship query-filter hardening closes a real latent production
  bug (any future enum removal or malformed row would otherwise recur the
  500) at a single, unmissable choke point.

**Negative / accepted limitations**

- **E2E is not a per-PR gate.** A regression introduced between nightly runs
  is caught up to ~24h later, or only on manual dispatch — an explicit
  trade against CI speed and stability, revisitable later if the suite
  proves stable and fast enough to promote to `pull_request:`.
- **Per-test login cost.** Every test pays a full Keycloak OIDC round trip;
  acceptable at 3 tests, a candidate for login-once-reuse (storageState
  reinjection or ROPC) if the suite grows or flakes.
- **Keycloak realm user-id pinning is a narrow, load-bearing assumption.**
  `deploy/keycloak/kartova-realm.json` pins `admin@orga.kartova.local`'s
  Keycloak user id (`601eecd8-1054-4432-a6c0-40d5d53a50df`) so it lines up
  with the corresponding Postgres `users` row seeded by
  `Kartova.Migrator`'s `DevSeed`. This holds on fresh CI volumes (and fresh
  local `docker compose down -v` resets) because both sides start from the
  same pinned values every time. It is **not** resilient to a realm import
  that regenerates ids, or to a Postgres volume that persists across a
  realm change — the two would silently desync. Other seeded realm users
  (e.g. `member@orga.kartova.local`) are **not** pinned and rely on the
  write-through cache to create their `users` row on first login; only
  `admin@orga` (used by every E2E login) and `team-admin@orga` (used by
  DevSeed's fixture rows, ADR-0101) are pinned today. This is a known,
  accepted fragility of the dev/CI seed story, not a production concern
  (production has no DevSeed).
- **Real-k8s URL injection remains an open gap.** The web image only works
  correctly against `localhost` defaults (`VITE_API_BASE_URL`,
  `VITE_OIDC_AUTHORITY` baked in at build time). There is no per-environment
  build-arg matrix and no runtime `config.js`-style injection. This gap
  predates this slice and is not solved by it; it is called out here because
  the rootless image change touches the same Dockerfile and would be the
  natural place to fix it later. Tracked as a follow-up, not blocking.
- **The query filter makes drifted rows silent and API-undeletable.** Rows
  with an unmappable `type` vanish from every read with no log or metric, and
  because delete-by-id inherits the same filter they return `404` and cannot
  be removed through the API — cleanup requires a direct SQL/migration pass
  (as the original `PurgePartOfRelationships` fix did). This is an accepted
  trade for now: drift is rare and operationally cleaned at the DB, matching
  prior practice. Follow-ups if drift proves recurrent: emit a warning/metric
  when unmappable rows exist for a tenant, and/or expose an OrgAdmin
  list/cleanup path via `IgnoreQueryFilters()`.
- **Additional journeys are deferred.** Search, graph explorer, notifications,
  and other flows have no E2E coverage yet; added incrementally as future
  regressions warrant, per the "thin E2E" testing strategy.

## References

- Spec: `docs/superpowers/specs/2026-07-08-e2e-test-infrastructure-design.md`
- Plan: `docs/superpowers/plans/2026-07-08-e2e-test-infrastructure.md`
- DoD ledger: `docs/superpowers/verification/2026-07-08-e2e-test-infrastructure/dod.md`
- ADR-0084 — Playwright MCP for frontend development (dev-time verification;
  boundary drawn above)
- ADR-0097 — MSTest supersedes xUnit / five-tier testing pyramid (this ADR
  realizes tier-5 E2E)
- ADR-0085 — migrations run via dedicated `Kartova.Migrator`, never at app
  startup (unchanged; `migrator` remains a compose dependency of `web`'s
  bring-up)
- ADR-0101 — team-admin authority via membership (source of the pinned
  `team-admin@orga` Keycloak/Postgres id used by DevSeed fixture rows)
- `docs/TESTING-STRATEGY.md` — five-tier pyramid, "E2E is thin" guidance
