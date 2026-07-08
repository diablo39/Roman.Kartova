# Slice — E2E Test Infrastructure (checked-in Playwright suite)

**Date:** 2026-07-08
**Stories:** E-01.F-02.S-03 (End-to-end test infrastructure)
**Phase:** 0/1 — Foundation
**Branch (proposed):** `feat/e2e-test-infrastructure`
**Governing decisions:** [ADR-0097](../../architecture/decisions/ADR-0097-mstest-supersedes-xunit.md) (five-tier pyramid — this slice realizes the tier-5 E2E layer), [ADR-0084](../../architecture/decisions/ADR-0084-playwright-mcp-for-frontend-development.md) (Playwright **MCP** for dev-time verification — distinct from this automated suite; §6 draws the boundary), plus a **new light ADR** proposed in §6 (compose-orchestrated E2E, rootless web container, nightly cadence). [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md) (E2E is thin — a few critical journeys).

---

## 1. Goal

Stand up the project's first **checked-in, automated E2E suite** (Playwright) that drives the real running stack through a real browser, and convert the two **gate-10 blocking defect classes** — which today have *zero* automated coverage — into regression tests:

1. **Post-login UI-state regression** — the "sunset-override checkbox unreachable before sunset" class (`2026-07-01-adr0073-cleanups`). Deterministic; a browser flow catches it.
2. **Production-data-shape drift** — the "500 on `GET /relationships` from a stray unknown-kind row" class (`2026-07-04-catalog-api-connectivity-edges`). Missed by every existing test *because fresh test DBs have no drifted rows*; only observing the running system against injected legacy data surfaces it.

Secondary goal: **retarget gate 10** from "drive deterministic flows by hand" to "exploratory + data-shape observation" — the classes E2E structurally cannot reach — with the nightly E2E suite owning the deterministic-flow regressions.

### 1.1 Why now / motivation trail

Gate-findings telemetry across 11 slices was scored (this conversation): the **manual/Playwright gate (gate 10)** had perfect precision and was the *only* source of runtime-only blocking bugs, but n=3 and it is human-driven. Analysis concluded E2E should **absorb the regression half** of gate 10 (deterministic flows → specs) while gate 10 shrinks to exploratory + drift observation. E2E infra did not exist; it was the unbuilt backlog story **E-01.F-02.S-03**. The user chose to build it now.

---

## 2. Pre-requisites (already on master)

- **Full backend stack in `docker-compose.yml`:** `postgres` (5432), `keycloak-db`, `keycloak` (8180→8080, dev realm `deploy/keycloak/kartova-realm.json`), `migrator`, `api` (8080). Migrations run via the `migrator` container (ADR-0085), never at app startup.
- **Web is already containerized:** `web/Dockerfile` (node build → nginx runtime), `web/nginx.conf` (SPA `try_files` fallback + cache headers). CI's `images` job already builds `kartova/web:ci`. **Not yet a running compose service.**
- **Web config is build-time inlined** (vite): `VITE_API_BASE_URL` defaults `http://localhost:8080`, `VITE_OIDC_AUTHORITY` defaults `http://localhost:8180/realms/kartova`, `VITE_OIDC_CLIENT_ID` defaults `kartova-web`. No runtime injection. `oidc-client-ts` stores the user in **sessionStorage** (`AuthProvider.tsx:12`).
- **DevSeed:** ~120 orgA applications; **no services/relationships** seeded (prior sessions). Login `admin@orga` / `dev_password_12` (OrgAdmin, full permission set).
- **Known SPA quirk (bug #47):** deep-link cold-loads bounce; must land on `/`, authenticate, then navigate in-SPA.
- **CI jobs today:** `backend`, `images`, `stryker-config-drift`, `frontend`, `helm`. No E2E job.

---

## 3. Design decisions (locked in brainstorming 2026-07-08)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Environment = full compose stack + rootless web container** (brainstorm option B). Playwright drives the served container at `http://localhost:4173`, not `vite preview`. | Tests the **actual shipped artifact** (nginx serving, SPA fallback, prod bundle, "does `kartova/web:ci` boot") — a deploy-only defect class `vite preview` cannot reach, and the same "bug the tests can't hit" bucket that motivated E2E. Image + nginx config already exist; delta is one compose service. |
| 2 | **Rootless web image:** `web/Dockerfile` runtime → `nginxinc/nginx-unprivileged:1.27-alpine`; `nginx.conf` `listen 80`→`8080`; `EXPOSE 8080`. Compose `web` service publishes `4173:8080`. | User requires rootless; image is k8s-bound anyway. Non-root can't bind <1024 → 8080. `COPY nginx.conf → /etc/nginx/conf.d/default.conf` and `dist/` serving unchanged (world-readable). A deliberate hardening of the **shared prod image** (also built by CI `images`), not E2E-only. |
| 3 | **No build args / no runtime injection.** Container built with defaults → calls `localhost:8080`/`8180`, which the host Playwright browser reaches via compose's published ports. | Build-time defaults already align with the compose stack. Real-k8s per-env URL injection is a **latent gap** (§8), out of scope. |
| 4 | **Auth = real UI login per test** via a shared `login()` fixture (`admin@orga`/`dev_password_12`). Lands on `/`, drives the Keycloak page, returns to `/`, navigates in-SPA. | Zero storageState/token plumbing (sessionStorage isn't persisted by Playwright `storageState`; ROPC needs client changes). Exercises the real OIDC callback every run. Fine for a 3-test suite (~1–2s/test). Upgrade to login-once-reuse later only if flake/slowness bites. The gate-10 bugs were **post-login**, so auth is plumbing, not the SUT. |
| 5 | **Project layout = top-level `e2e/`** as its own npm project (own `package.json`, `@playwright/test` + `pg`), *not* inside `web/`. | Isolates Playwright deps from web's vite/vitest (avoids the lightningcss `npm ci` EPERM interaction). E2E is cross-cutting — drives the whole stack, not a web unit. |
| 6 | **Drift injection = per-test `pg` fixture, insert + teardown-delete**, scoped to the drift test only, tenant GUC set (owner conn / `NO FORCE`→`FORCE` toggle per the C2-migration pattern). | The unknown-kind row is *exactly* what 500s the relationships surface — a global seed would break the smoke/override tests. Must be isolated. No API path exists (enum value removed), so DB is the only route; a guarded test-seed endpoint was rejected as extra backend surface. |
| 7 | **Three journeys only** (TESTING-STRATEGY "thin"): smoke (login → Applications list renders), lifecycle-override regression, relationship-drift graceful-degrade. | Covers both gate-10 blocking classes + one end-to-end wire-up proof. |
| 8 | **CI cadence = nightly `schedule:` + `workflow_dispatch:`, NOT `pull_request`.** | Full stack on the runner is minutes + inherits Keycloak/compose saturation flake. Nightly regression net keeps per-PR CI fast. **Consequence:** E2E is *not* a gate-11 check on ordinary slice PRs; for **this** slice's own DoD it is verified via a manual `workflow_dispatch` run + local `e2e/run.sh` evidence. |
| 9 | **Gate-10 retarget is part of this slice** (CLAUDE.md edit + CHECKLIST tick + proposed ADR). | The doc change is the point of the analysis that started this; shipping the suite without retargeting gate 10 would leave the process guidance stale. |

---

## 4. Architecture

### 4.1 Repository additions

```
e2e/
  package.json                 @playwright/test, pg (isolated project)
  playwright.config.ts         baseURL http://localhost:4173; retries 2; workers 1; html reporter + trace on-first-retry
  fixtures/
    auth.ts                    login(page): goto '/', KC form (admin@orga/dev_password_12), await return to '/', in-SPA nav helpers
    db.ts                      withDriftEdge(): pg connect → insert unknown-kind relationship row (tenant GUC) → yield → delete
  tests/
    smoke.spec.ts              login → /applications list renders (≥1 row from DevSeed)
    lifecycle-override.spec.ts before-sunset app → lifecycle menu → Decommission → override checkbox reachable/visible
    relationship-drift.spec.ts withDriftEdge → open the entity's relationships surface → asserts it renders (no 500, graceful degrade)
  run.sh                       docker compose up -d --build (pg,keycloak,migrator,api,web) → wait-healthy → playwright test → (report)
web/Dockerfile                 runtime base → nginx-unprivileged; EXPOSE 8080
web/nginx.conf                 listen 8080
docker-compose.yml             + web service (build web/Dockerfile, 4173:8080, depends_on api+keycloak)
.github/workflows/ci.yml       + e2e job (schedule + workflow_dispatch); artifacts: playwright-report, traces
scripts/ci-local.sh            + optional `e2e` subcommand (local parity)
CLAUDE.md                      gate 10 reworded (§6)
docs/product/CHECKLIST.md      E-01.F-02.S-03 ticked
docs/architecture/decisions/   new ADR (proposed, previewed before save)
```

### 4.2 Bring-up flow (`e2e/run.sh`, identical locally and in CI)

```
docker compose up -d --build postgres keycloak-db keycloak migrator api web
  → wait: api /health 200, keycloak realm reachable, web index 200 at :4173
  → (npm ci in e2e/) → npx playwright test
  → on failure: HTML report + traces retained/uploaded
```

### 4.3 Drift fixture (`db.ts`) — isolation contract

- Opens a `pg` connection to `localhost:5432` (compose postgres, creds from compose env).
- Sets tenant context (`SET app.current_tenant_id` / RLS toggle mirroring `DevSeed`/the C2 migration).
- Inserts one relationship row with a **kind the current mapper does not recognize** (the "legacy `PartOf`" shape). The exact token is whatever reproduces "unknown kind" — confirmed against the relationships table's storage (text vs enum) in the plan.
- `yield` to the test; on teardown, deletes the row by id. Guarantees no other test sees it.

---

## 5. Testing strategy (per docs/TESTING-STRATEGY.md)

This slice *is* the tier-5 layer; its "tests" are the E2E specs themselves. Altitude rule respected: E2E asserts only top-of-stack, human-visible behavior (a rendered list, a reachable control, a non-500 surface) — never seam-level concerns (`SET LOCAL`, issuer/audience) that live in integration tests.

**Gate-5 (real-seam) artifacts named as deliverables:**
- `e2e/tests/smoke.spec.ts` — happy path (login + list render).
- `e2e/tests/lifecycle-override.spec.ts` — deterministic UI-state regression (gate-10 class #1).
- `e2e/tests/relationship-drift.spec.ts` — negative/degrade case against injected drifted data (gate-10 class #2).
- `e2e/fixtures/auth.ts`, `e2e/fixtures/db.ts` — real Keycloak login + real Postgres/RLS drift injection (the real seams).
- **Container-build coverage:** the `web/Dockerfile` base-image change is exercised by CI's existing `images` job (builds `kartova/web:ci`) *and* by `e2e/run.sh` building + booting the `web` service — the runtime the image never otherwise gets asserted on.

Each journey has ≥1 explicit assertion with a strong oracle (visible row / reachable control / rendered-not-500). Retries (2) absorb known compose/KC saturation flake without masking real failures (trace-on-retry preserved).

---

## 6. Gate-10 retarget (doc deliverable)

- **CLAUDE.md gate 10** — reword "Visual/API verification — drive the change end-to-end… navigate in-SPA" to center **exploratory + data-shape observation**: the classes E2E can't reach (drifted/legacy production data, unknown-unknowns, first-time visual surfaces). Add: *deterministic user-flow regressions belong in the nightly Playwright E2E suite; converting a gate-10 finding into an E2E spec is the expected follow-up (per the "any bug it finds becomes a regression test" rule).* Gate 10 stays a per-slice human/MCP pass; it does not fold into E2E (both are different lenses — the no-folding rule).
- **Boundary vs ADR-0084:** ADR-0084 = Playwright **MCP**, interactive dev-time verification (gate 10). This suite = checked-in automated Playwright (`@playwright/test`), nightly. The proposed ADR states the boundary so the two Playwrights aren't conflated.
- **CHECKLIST.md** — tick `E-01.F-02.S-03` with a one-line summary.
- **New ADR (proposed, previewed before save):** "E2E suite — compose-orchestrated, rootless web container, real-UI-login-per-test, nightly cadence." Records: option-B environment, rootless base image, per-test drift fixture, nightly/dispatch (not per-PR) and why, and the gate-10 boundary. Per the ADR rule, previewed for approval before writing.

---

## 7. Scope / slice size

Production **business** code changed: none (Dockerfile/nginx/compose are infra; no Domain/Application diff). The bulk is **test code** (excluded from the ~400/800-LOC count). Well under ceiling; no decomposition.

- **Gate 6 (mutation): N/A** — no Domain/Application logic in the diff.
- **Gate 3 (real-seam integration): satisfied by the E2E suite itself** (this slice's seam is the running stack).
- **Gate 11 (CI green on PR):** the E2E job is nightly/dispatch, so it is **not** an automatic PR gate; verified for this slice via a manual `workflow_dispatch` run + local `e2e/run.sh` evidence (recorded in the DoD ledger).

---

## 8. Out of scope / follow-ups

- **Real-k8s URL injection** — the web image only works with `localhost` defaults (no per-env `VITE_*` build args, no runtime `config.js`). A genuine prod-config gap; recorded as a separate follow-up, **not** solved here.
- **Login-once-reuse** auth optimization (storageState re-inject / ROPC) — deferred; adopt only if per-test login proves slow/flaky.
- **Additional journeys** (search, graph explorer, notifications) — add incrementally as regressions warrant.
- **Flipping E2E to per-PR blocking** — revisit once the suite proves stable nightly.
- **Sub-slice B of nothing** — this is a single slice; no decomposition.

---

## 9. Success criteria

1. `e2e/run.sh` brings up the full stack incl. the rootless `web` container and runs all 3 specs green locally.
2. `nginxinc/nginx-unprivileged` serves the app; container runs as non-root; CI `images` job still green.
3. The override spec **fails** against a reintroduction of the gate-10 bug (menu ignores override perm) and passes on fixed code.
4. The drift spec **fails** (500) if the `isRenderableKind` guard is reverted, and passes (graceful) with it.
5. Nightly `e2e` CI job + `workflow_dispatch` present; a manual dispatch run is green (artifacts uploaded).
6. Gate 10 reworded in CLAUDE.md; `E-01.F-02.S-03` ticked; ADR previewed + (on approval) saved.
