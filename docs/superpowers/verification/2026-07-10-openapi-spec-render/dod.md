# DoD Ledger — OpenAPI Spec Render (E-11.F-02.S-01)

**Slice:** `2026-07-10-openapi-spec-render` · **Branch:** `feat/catalog-openapi-spec-render` · **HEAD:** `8cc4bab`
**PR:** [#69](https://github.com/diablo39/Roman.Kartova/pull/69) · **Last updated:** 2026-07-10
**Spec:** `docs/superpowers/specs/2026-07-10-openapi-spec-render-design.md`
**Plan:** `docs/superpowers/plans/2026-07-10-openapi-spec-render.md`
**Findings telemetry:** `./gate-findings.yaml`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-07-10 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-07-10 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-07-10 |
| 4 Container build (images CI) | ✅ PASS | 2026-07-10 |
| 5 `/simplify` | ✅ PASS | 2026-07-10 |
| 6 Mutation (conditional) | N/A | 2026-07-10 |
| 7 `requesting-code-review` | ✅ PASS | 2026-07-10 |
| 8 `review-pr` | ✅ PASS | 2026-07-10 |
| 9 `deep-review` | ✅ PASS | 2026-07-10 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-07-10 |
| 10 Visual / API verification (ADR-0084) | ✅ PASS | 2026-07-10 |
| 11 CI green on PR (`ci-local.sh` = pre-push mirror) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** `cd web && npm run build` (tsc -b && vite build) — 0 type errors, build succeeded. Re-run by controller (not just implementer). Chunk-size warning only (see gate-findings F-1, non-blocking). Note: C# solution untouched (frontend-only) — the .NET `TreatWarningsAsErrors` build is unaffected by this slice.
**At:** 68af12a / 2026-07-10

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** `pr-review-toolkit:code-reviewer` vs plan/spec — spec compliance ✅ (all 5 tasks match, no under/over-build, no scope creep beyond the 9 named files), code quality approved, no Critical/Important. One minor (hand-rolled toggle — no reusable primitive exists, matches plan).
**At:** fa3ff7f / 2026-07-10

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS
**Evidence:** `cd web && npm run test` (Vitest) — **114 files / 808 tests passed, 0 failures**. Re-run by controller. Real-seam integration **N/A** — frontend-only slice, no HTTP/auth/DB/middleware change (spec §7). New tests: `detectSpecKind` truth table (11 cases), `OpenApiRender` error-boundary fallback, `ApiSpecSection` toggle/fallback (3 cases).
**At:** 68af12a / 2026-07-10

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** `docker compose build web` — exit 0. The new `@scalar/api-reference-react` dep resolves + installs in the container `npm ci` and the web image builds clean (the seam unit tests can't reach). Docker 29.5.3.
**At:** fa3ff7f / 2026-07-10

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS
**Evidence:** 4 parallel cleanup agents (reuse/simplification/efficiency/altitude). Applied: hoisted IIFE→consts, `useMemo(detectSpecKind)`, toggle group `role`/`aria-label` (fa3ff7f). Skipped w/ reason: drop `_mediaType` (intentional per design §4). Deferred to review gate: full react-aria `Tabs` conversion. See `gate-findings.yaml`.
**At:** fa3ff7f / 2026-07-10

### 6 — Mutation loop (conditional: Domain/Application changes only)
**Status:** N/A
**Evidence:** No C# Domain/Application change (frontend-only). `detectSpecKind` is TypeScript — Stryker.NET does not cover TS; its branch logic is fully exercised by the 11-case truth-table unit test.
**At:** 2026-07-10

### 7 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** Holistic review — no blocking; architecture sound (single Scalar swap point confirmed, lazy split correct, layer order correct). Should-fixes addressed in 82d1744: happy-path test locks `hideClientButton` read-only config; `componentDidCatch` logging added. Should-fix carried to gate-10: verify `hideClientButton` hides try-it-out on **every operation panel** (Scalar #7741) — CSS-hide fallback ready if it reproduces. Nits (dead `mediaType` prop, `rawView` memo) resolved.
**At:** 82d1744 / 2026-07-10

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS
**Evidence:** `silent-failure-hunter` — 2 real defects, both fixed in 82d1744: (CRITICAL) error boundary now logs via `componentDidCatch` (was silent); (HIGH) `key={content}` resets the boundary on spec replace/change (was stuck on stale `failed:true`). LOW (non-object JSON caught path) hardened with an explicit object guard. New tests cover the reset + config-lock.
**At:** 82d1744 / 2026-07-10

### 9 — `deep-review`
**Status:** ✅ PASS
**Evidence:** Fixed-schema deep review vs spec/plan/ADR-0094/ADR-0084. **1 Blocking (real, fixed ec5cc3d):** `hideClientButton` alone does NOT disable try-it-out — `hideTestRequestButton` (defaults shown) controls the inline live "Test Request" button = the actual SSRF/live-request surface (spec §6). Added `hideTestRequestButton: true` + test lock + false-positive regression case. Confirmed clean: CSS layer order (Tailwind utils win over Scalar base), detection truth table (top-level-key-only, no value substring FP), no `dangerouslySetInnerHTML`, lazy bundle isolation. Nit → gate-10: verify no Scalar `properties`-layer visual bleed.
**At:** ec5cc3d / 2026-07-10

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ✅ PASS
**Evidence:** After gate 5/7/8/9 fixes (fa3ff7f, 82d1744, ec5cc3d): `npm run build` clean (Scalar isolated in OpenApiRender-*.js chunk, not main), `npm run test` **114 files / 811 tests passed** (was 808 + 3 new: config-lock, reset, FP-regression).
**At:** ec5cc3d / 2026-07-10

### 10 — Visual / API verification (observe the running system)
**Status:** ✅ PASS (found + fixed 1 real read-only gap → 8cc4bab)
**Method:** Cold-started vite dev (ADR-0084) on the live stack (api/keycloak/postgres up), authenticated `admin@orga.kartova.local`, drove the API detail page in-SPA. Evidence: `gate10-openapi-rendered.png`, `gate10-openapi-rendered-readonly.png`, `gate10-asyncapi-raw-fallback.png` (siblings).
**Verified:**
- **OpenAPI renders by default** — "aaaapi" (REST) → Scalar reference, "Rendered" toggle pressed; `role=group aria-label="Spec view"` present.
- **Toggle round-trips** — Rendered→Raw (JSON badge + Copy + `{"openapi":"3.0.0",…}`)→Rendered.
- **Non-OpenAPI fallback** — "Orders Events (Async)" (AsyncAPI) → **no toggle, no Scalar, raw `<pre>` + YAML badge** (`asyncapi: 3.0.0…`). Detection `other` confirmed live.
- **XSS sanitized** — injected `<img onerror>` + `<script>` in spec descriptions → `window.__xss*` all null, 0 `<script>` in scope, no `onerror` in DOM; descriptions render as plain text.
- **0 console errors** (errors + warnings = 0).
- **No Scalar visual bleed** (gate-9 nit) — reference renders cleanly in its container; no global style leak observed.
- **Read-only GAP FOUND + FIXED (real):** `hideTestRequestButton` did NOT remove the inline "Send Request" API client (Scalar #7741) — visible send controls + Cookies/Headers/Query were reachable = live-request surface (spec §6). Added scoped CSS (`.scalar-render .scalar-client, [data-addressbar-action=send]` → `display:none`). Re-verified with the committed CSS (probe removed): **0 visible send/test controls, client dialog hidden, reference + code samples intact**. Coupled to Scalar internals → re-verify on upgrade; **follow-up FU-1: Playwright E2E regression** asserting no visible Send control.
- Seed hygiene: restored aaaapi's original spec (removed the XSS probe from the dev DB).
**At:** 8cc4bab / 2026-07-10

### 11 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING — pre-push mirror `scripts/ci-local.sh frontend` **PASS** (Release: npm ci → codegen → typecheck → test → build). PR [#69](https://github.com/diablo39/Roman.Kartova/pull/69) pushed; awaiting runner.
