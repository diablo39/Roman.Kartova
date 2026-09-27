# DoD Ledger — E-01b.F-03.S-01 Platform admin identity & API isolation

**Slice:** `E-01b.F-03.S-01 Platform admin identity & API isolation` · **Branch:** `feat/e01b-s1-admin-isolation`
**Spec:** `docs/superpowers/specs/2026-09-25-e01b-s1-platform-admin-isolation-design.md` · **ADR:** ADR-0118
**Plan:** `docs/superpowers/plans/2026-09-25-e01b-s1-platform-admin-isolation-plan.md` (local, gitignored)
**Findings telemetry:** `./gate-findings.yaml`
**Terminal commit:** `4eaea99` (code); evidence commits follow

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A. FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-27 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-09-26 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-09-26 |
| 4 Container build (images CI) | ✅ PASS | 2026-09-26 |
| 5 `/simplify` | ✅ PASS (advisory) | 2026-09-26 |
| 6 `requesting-code-review` | ✅ PASS (after fix wave) | 2026-09-26 |
| 7 `review-pr` | ✅ PASS (after fix wave) | 2026-09-27 |
| 8 `deep-review` | ✅ PASS (after fix wave) | 2026-09-27 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-09-27 |
| 9 Visual / API verification (ADR-0084) | ✅ PASS | 2026-09-27 |
| 10 CI green on PR (`ci-local.sh` = pre-push mirror) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** `cmd //c "dotnet build Kartova.slnx -warnaserror"` → `0 Warning(s) 0 Error(s)`. Passed at 87d6b22 (first run) and again at the terminal commit 4eaea99.
**At:** 4eaea99 · 2026-09-27

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** Every task (T0–T7) had a fresh task reviewer, with spec and quality verdicts, via SDD. Opus reviewed the security-critical T3; sonnet or haiku reviewed the rest. Per-task review packages and reports sit in the SDD workspace (local).

| Task | Result |
|---|---|
| T0, T1, T2, T3, T6 | review clean |
| T4 | 1 fix round — GUID `sub` in the key regression test (42bcf1f) |
| T5 | 1 fix round — `DuplicateObject` guard in `AuthSmokeTests` (f0dc461) |
| T7 | controller resolved the ⚠️ item (the OpenAPI snapshot models no security anywhere) |

**At:** 87d6b22 · 2026-09-26

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS
**Evidence:** `dotnet test Kartova.slnx -m:1 --no-build` → 15 assemblies, 1780/1780, 0 failed at 87d6b22.
- The first run was killed by a session restart. Docker Desktop was restarted and the re-run was clean.
- **Real seam:** real `JwtBearer` against real KeyCloak tokens in `PlatformRealmLiveTokenTests` (both realms, forged-key token) and `HealthCheckEndpointTests`, plus real Postgres through the fixtures.
- Proof of the two-realm KeyCloak fixture: the Api, Organization and Identity integration assemblies are green.
**At:** 87d6b22 · 2026-09-26

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** `docker compose build` → `kartova/web:dev`, `kartova/api:dev` and `kartova/migrator:dev` Built, `EXIT=0`.
- The strict N/A criteria were met (no Dockerfile or restore change), but the gate ran anyway because the spec named the compose change.
- The API image was rebuilt again at the terminal commit by the gate-9 `docker compose up -d --build`.
**At:** 87d6b22 · 2026-09-26

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS (advisory)
**Evidence:** `./simplify.md`. Four angles, 8 findings:
- 2 applied (fe1520d);
- 4 rejected, each with a reason;
- 2 deferred as TD-014 and TD-015.

0 correctness catches.
**At:** fe1520d · 2026-09-26

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS (after fix wave)
**Evidence:** `./requesting-code-review.md`. The whole-branch opus review returned "With fixes": 2 Important findings (the AllowAnonymous hole in the arch rule; runbook gaps) plus minors.
- Fix wave: 7fc93f6 and bb0c3f3.
- Scoped re-review: 5/5 addressed.
**At:** bb0c3f3 · 2026-09-26

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS (after fix wave)
**Evidence:** `./review-pr.md`.
- Agents: code-reviewer, pr-test-analyzer, type-design-analyzer and silent-failure-hunter. silent-failure-hunter ran because the diff adds error handling; comment-analyzer was skipped because the diff has few code comments.
- Findings: 0 Critical, no fail-open path.
- Fix wave: f6227dc..9c5605a. It pins the exact combined scheme set, adds the forged-token tests, `/health/detailed` cases, exact fail-fast messages, `RequireHttpsMetadata`/`MetadataAddress` tests and observability.
- Scoped re-review: all addressed.
**At:** 9c5605a · 2026-09-27

### 8 — `deep-review`
**Status:** ✅ PASS (after fix wave)
**Evidence:** `./deep-review.md`. 0 blocking / 1 should-fix / 5 nits / 3 missing tests.
- The should-fix: ADR-0092 and ADR-0006 lacked notes that ADR-0118 amends them.
- Fix wave: 84b2067 and 4eaea99. It adds the ADR amendment notes, the spec status, comments, issuer-only/audience-only 401 tests, and the direct-grants client-set rule.
- Rejected: nit 5 (a `ForwardDefaultSelector` would authenticate operator tokens on tenant routes) and missing-test 3 (fixture `/health/detailed` 200; the live test covers it).
- Scoped re-review: all addressed.
**At:** 4eaea99 · 2026-09-27

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ✅ PASS
**Evidence:** build `-warnaserror` 0/0; `dotnet test Kartova.slnx -m:1 --no-build` → 15 assemblies, 1794/1794, 0 failed.
**At:** 4eaea99 · 2026-09-27

### 9 — Visual / API verification (observe the running system)
**Status:** ✅ PASS
**Evidence:** `./gate9-live-api.txt`. Cold `docker compose up -d --build`, with the live curl matrix run against real KeyCloak tokens (tokens withheld).

| Request | Result |
|---|---|
| platform token → `/api/v1/admin/session/me` | 200 (`platform-admin@kartova.local`, "Platform Admin") |
| tenant token → admin session | 401, `invalid_token` (issuer invalid) |
| platform token → `/api/v1/organizations/me` | 401, `invalid_token` |
| tenant token → org | 200 |
| platform token → `/health/detailed` | 200 |
| tenant token → `/health/detailed` | 401 |
| anonymous | 401, bare `Bearer` |

**Exploratory finding (real data drift):** the local `keycloak-db` volume predates the slice. Its `kartova` realm still holds `platform-admin@kartova.local` together with the **real** `platform-admin` role. That real token → **401** on both admin surfaces, so the isolation fails closed under drift.
- Action taken: a local-dev `down -v` note in `deploy/README.md`.
- The prod equivalent is covered by runbook step 5.

**Not applicable here:**
- UI: there is no UI surface in S1 (`web-admin` is S2).
- E2E-impact: `e2e/` has no platform-admin or admin-route usage, and the tenant login flow is unchanged.
**At:** 4eaea99 · 2026-09-27

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING
**Evidence:** pre-push `scripts/ci-local.sh`, then the PR CI run.
**At:** —

## Deviations from spec (recorded)
- **Helm:** unchanged. The chart carries no `Authentication:*` env even for tenants; the runbook documents the keys (spec updated).
- **`/health/detailed` "tenant token with `platform-admin`" case:** impossible against live KeyCloak once the role is removed. It is covered by the fixture test `Tenant_token_claiming_platform_admin_gets_401_on_health_detailed` and by the gate-9 drift observation.
- **Tenant route in the isolation tests:** `/api/v1/organizations/me` instead of `/api/v1/catalog/applications` (spec updated).
