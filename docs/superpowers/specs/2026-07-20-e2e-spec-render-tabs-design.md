# Design — E2E regression specs: spec-render read-only lock + tab-switch (deferred FU-1s)

**Date:** 2026-07-20
**Topic:** `e2e-spec-render-tabs`
**Epics/stories:** closes the two deferred follow-ups
- **E-11.F-02.S-01 FU-1** (openapi-spec-render, PR #69) — "Playwright E2E regression for the read-only lock."
- **E-11.F-02.S-04 FU-1** (tabbed-entity-detail, PR #70) — "Playwright E2E for the tab-switch happy path."

Both were deferred at merge; this slice writes them. Motivated by the **#70 nightly-drift retro** (CLAUDE.md E2E-impact trigger): shipped user flows that lack a nightly net drift silently.

---

## 1. Goal

Add two Playwright specs to the nightly `e2e/` suite that lock the two shipped, unit-tested-but-not-E2E'd behaviours against regression on the **real stack** (real Keycloak, real Postgres/RLS, real API + web images):

1. **Read-only lock** — on an API's **Definition** tab, Scalar renders the OpenAPI spec, but the live "Send / Test Request" client is **not reachable**. This is the SSRF / live-request surface; gate-10 on #69 found that Scalar config flags alone (`hideClientButton` + `hideTestRequestButton`) leave the inline client mounted (Scalar issue #7741), fixed by `specRender.css` overriding `.scalar-client` and `[data-addressbar-action="send"]`. A unit/jsdom test cannot prove the *rendered browser* hides these — only a real-browser E2E can.
2. **Tab-switch happy path** — `DetailTabs` (ADR-0114) drives `?tab=` (replace-writes), mounts only the active panel, and normalizes a present-but-invalid `?tab` to the default (Overview).

Non-goal: changing any production component. `DetailTabs` and `SpecRender` shipped and are covered by unit tests; this slice only adds E2E coverage and the fixture data they need.

---

## 2. Blocker and decision — fixture data

Both specs need an **API entity with a spec document** to reach the Definition tab. Current `DevSeed.cs` seeds Org A, users/teams, 120 applications, and one `E2E Sunset Override Fixture` **application** — **no API, no `catalog_api_specs` row**. The `/catalog/apis` list is empty in the E2E stack today.

**Decision (approved 2026-07-20): Approach A — seed a fixed-id API + spec in `DevSeed.cs`.**

Rationale: a benign, stable, read-only API+spec is exactly the shape of the existing sunset-app fixture, which lives in DevSeed with a pinned id mirrored in `e2e/fixtures/nav.ts`. The `insertDriftEdge` pg-bypass pattern (Approach B) is reserved for the *deliberately broken* drift row we don't want lingering in `docker compose up` demos. A valid demo API belongs in the seed.

**One fixture serves both specs** (approved): the API detail page carries all three tabs (Overview · Dependencies · Definition), so the tab-switch spec exercises the full tab set including the lazy-loaded Definition chunk, and the read-only-lock spec uses the same Definition tab.

---

## 3. Components / changes

### 3.1 `src/Kartova.Migrator/DevSeed.cs` (C#)
Add, after the sunset-fixture app block, a fixed-id API + its 1:1 spec doc. Idempotent via `ON CONFLICT (id) DO NOTHING`, run on every DevSeed invocation (outside the `existing == 0` first-seed guard), owned by the already-seeded Demo Team (`DemoTeamId`), created by `TeamAdminUserId`. RLS toggled `NO FORCE` → insert → `FORCE` in a `try/finally`, matching the surrounding blocks.

- `catalog_apis` row:
  - `id` = `e2e00000-0000-0000-0000-000000000010` (fixed, distinct from the app fixture `…0001`)
  - `display_name` = `"E2E Spec Render Fixture"`
  - `description` = short fixture note
  - `style` = `(short)ApiStyle.Rest` (pinned enum, not a literal — mirrors the `(short)Lifecycle.Deprecated` cast)
  - `version` = `"1.0.0"`, `spec_url` = NULL
  - `team_id` = `DemoTeamId`, `created_by_user_id` = `TeamAdminUserId`
- `catalog_api_specs` row (1:1, unique `api_id`):
  - `id` = fixed GUID (`e2e00000-0000-0000-0000-000000000011`)
  - `api_id` = the API id above, `tenant_id` = `OrgATenantId`
  - `media_type` = `"application/json"`
  - `content` = a **minimal valid OpenAPI 3.0 document** with top-level `"openapi": "3.0.x"` (so `detectSpecKind` renders it by default, not the raw fallback) and **at least one path + operation** (so an operation-level "Test Request" surface actually exists to prove it is suppressed). A deterministic title (e.g. `"E2E Fixture API"`) gives the render an assertable heading.

### 3.2 `e2e/fixtures/nav.ts` (TS)
Add API-fixture constants + navigation, mirroring the app helpers and the "keep three files in sync" doc-comment convention:
- `FIXTURE_API_ID`, `FIXTURE_API_NAME` (= the DevSeed values above)
- `API_DETAIL_URL` = `/\/catalog\/apis\/[0-9a-f-]+$/`
- Navigation: specs **deep-link** via `page.goto` to the fixed id (the #47 returnTo round-trip supports cold deep-loads) — robust and independent of list pagination/search-box selectors. A `findFixtureApiLink` list-search helper is optional and only added if a spec chooses in-SPA nav.

### 3.3 `e2e/tests/spec-render-readonly.spec.ts` (new)
1. `login(page)`.
2. `page.goto` the fixture API detail with `?tab=definition` (or click into it in-SPA, then open Definition).
3. Assert the Definition panel **rendered the spec, not the raw fallback**: the Scalar container `.scalar-render` is present **and** a known token from the fixture spec (e.g. the `"E2E Fixture API"` title or an operation summary) is visible. Also assert the "Couldn't render this spec — showing source." warning is **absent**.
4. **Read-only lock assertions** (the regression core):
   - `.scalar-render .scalar-client` has count 0 **or** is not visible.
   - `.scalar-render [data-addressbar-action="send"]` is not visible.
   - No visible control with an accessible name matching `/send request|test request|send$/i` inside the render scope.
5. Console-error assertion (ADR-0084 hygiene): no unexpected console errors during render.

### 3.4 `e2e/tests/detail-tabs.spec.ts` (new)
On the fixture API detail page:
1. `login`, navigate to detail root (no `?tab`).
2. Default: the **Overview** tab is selected and its content is visible; Dependencies/Definition panel content is **not** in the DOM (only the active panel mounts).
3. Click **Dependencies** → URL gains `?tab=dependencies` (replace, no history spam), Dependencies content visible (empty-state is fine — no relationship fixtures), Overview content gone.
4. Click **Definition** → URL `?tab=definition`, spec render appears (lazy Definition chunk loads).
5. Deep-link normalization: `page.goto(detail + '?tab=bogus')` → URL normalizes to `?tab=overview`, Overview shown.

---

## 4. Data flow

`docker compose up` → migrator runs `DevSeed` → fixture API + spec rows present in Org A → real API serves `GET /catalog/apis/{id}` + spec → web SPA renders detail page with `DetailTabs`; the Definition tab lazy-loads `SpecRender` → Scalar renders read-only. Playwright drives Chromium against `http://localhost:4173` (web image) with real Keycloak login and asserts on the live DOM.

---

## 5. Error handling / edge cases

- **Idempotency:** `ON CONFLICT (id) DO NOTHING` on both rows → re-seeding a running dev DB is safe; the fixture stays read-only (never mutated by tests) so it's stable across runs.
- **RLS:** inserts bracketed by `NO FORCE` / `FORCE` in `try/finally` (owner role lacks BYPASSRLS), identical to the existing DevSeed blocks.
- **Raw-fallback masking:** asserting a spec-derived token (not just `.scalar-render` presence) prevents a false green where Scalar silently degraded to the raw `<pre>` (which would also hide the client, passing the read-only checks for the wrong reason).
- **Scalar internals coupling:** `.scalar-client` / `data-addressbar-action="send"` are Scalar-version-coupled (documented in `specRender.css`); the spec comment cross-references that file so a Scalar upgrade re-verifies both.

---

## 6. Testing strategy (per docs/TESTING-STRATEGY.md)

This slice **is** test code (tier-5 E2E) plus a dev-fixture seed. Real-seam is inherent — the specs run against real Keycloak + Postgres/RLS + the real API/web images via `e2e/run.sh`.

Gate-artifact deliverables:
- Two new `e2e/tests/*.spec.ts` (≥1 positive path each; the read-only spec's client-absent checks are the negative-of-a-negative guard; the tabs spec's `?tab=bogus` normalization is its negative case).
- `DevSeed.cs` fixture — verified present by the specs themselves (they fail red if the seed row is missing).
- No new backend unit/integration tests: no production C# logic changes (DevSeed is dev-fixture wiring, not business logic); `DetailTabs`/`SpecRender` unit coverage already shipped.

**Run locally at gate 10** via `e2e/run.sh spec-render-readonly.spec.ts detail-tabs.spec.ts` (or full `e2e/run.sh`) and record in the DoD ledger — these join the nightly suite thereafter.

---

## 7. DoD

The ten always-blocking gates in CLAUDE.md apply. Notable per-gate shape:
- **Gate 1/3 (build + suite):** C# change (DevSeed) → full solution build + backend suite must stay green; web build unaffected (no web src change).
- **Gate 4 (container images):** the **migrator image** now seeds the fixture — the `images` CI job builds it; DevSeed runs at container init in the E2E compose stack.
- **Gate 6 (mutation):** N/A — no Domain/Application logic changed (DevSeed is fixture wiring).
- **Gate 10 (visual/API + E2E):** run the two specs locally against the real stack; this **is** the E2E-impact-trigger deliverable, not a fold of it.
- Others (2, 5, 7, 8, 9, 11) as usual.

DoD ledger + evidence: `docs/superpowers/verification/2026-07-20-e2e-spec-render-tabs/`.

---

## 8. Out of scope

- No changes to `DetailTabs`, `SpecRender`, or any production component.
- No relationship/service fixtures (Dependencies empty-state suffices).
- gRPC/GraphQL spec rendering, versioned docs (separate stories E-11.F-02.S-02/S-03).
- Promoting the nightly suite to a per-slice blocking gate (it stays nightly).
