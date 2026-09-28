# Gate 7 — `/pr-review-toolkit:review-pr` — S2 web-admin

**Range:** `2c03a2b...9b8cd83` · **Date:** 2026-09-28

**Agents run (4, in parallel):**
- the CLAUDE.md standing set: `type-design-analyzer`, `pr-test-analyzer`, `code-reviewer`;
- `silent-failure-hunter`, because the diff adds error-handling branches (session gate 401/403/5xx/network, the callback error path, and the throws in `CorsOriginLists`).

`comment-analyzer` was skipped: the diff is code-heavy, and the docs were already reviewed at gates 2 and 6.

## Results per agent

| Agent | Result |
|---|---|
| code-reviewer | **0 findings** at confidence ≥ 80. It verified the CORS wiring and order, the arch rules in both directions, that the `web/arch` tests are actually discovered by vitest, Helm backward compatibility, and the coverage-exclusion rule. |
| type-design-analyzer | 2 Important, 5 Suggestions |
| pr-test-analyzer | 7 gaps (criticality 2–6). No test asserts nothing, and none tests a mock instead of behavior. |
| silent-failure-hunter | 2 High, 2 Medium, 2 Low |

## Findings and disposition

| Source | Sev | Finding | Disposition |
|---|---|---|---|
| silent-failure | High | `signinRedirect` / `signoutRedirect` promises are discarded with `void`. If KeyCloak is unreachable during the redirect, the UI hangs silently on "Signing in…". | ~~fixed: `.catch` + `console.error`~~ **correction (gate 8):** the `.catch` was dead code, because react-oidc-context 3.3.1 navigators never reject: they set `auth.error` and resolve `null`. Re-done in the gate-8 fix: guard reset + log driven by `auth.error`, and a "Sign-in unavailable" panel. |
| silent-failure + type-design | High | No re-entrancy guard on the 401 handler: concurrent 401s fire `signinRedirect` several times, and the state/PKCE writes can race | **fixed:** `redirectingRef` guard in the shared `useApiAuthBridge` + a direct hook test |
| type-design | Important | "Never render a blank identity" is enforced at one call site, and the avatar showed "—" as initials | **fixed:** `orDash` moved to `lib/utils/format`; `TopBarFrame` applies it to the menu lines and computes initials from the raw name |
| silent-failure | Medium | `session/me` 5xx, network and contract-violation errors are never logged | **fixed:** one `console.error` per error, via an effect |
| pr-test | 6 | `post.logout.redirect.uris` has no test | **fixed:** seed-rule assertion |
| pr-test | 5 | `adminHtmlTarget` branches untested: undefined method, HEAD, undefined url, the `/__` prefix, hash + query | **fixed:** cases added |
| pr-test | 5 | The boundary test has no positive control | **fixed:** tests for regex extraction and forbidden-path resolution |
| pr-test | 3 | The CORS matrix row "unknown origin → admin route" is untested | **fixed:** preflight test |
| pr-test | 3 | `render-check.sh` only checks `webAdmin.enabled=false` | **fixed:** symmetric `web.enabled=false` check |
| type-design | Important | `CorsOriginLists` should be a `ValidatedCorsOrigins` wrapper type | **skipped:** there is a single composition root, and the host-boot overlap test (5e7b770) pins the wiring. YAGNI. |
| type-design | Suggestion | Test that `TenantWeb` ≠ `AdminWeb`; `AdminMeResponse` nullability; branded `__status`; bridge double-registration guard | **skipped:** speculative, a pre-existing pattern, or a backend contract decided in S1 |
| pr-test | 4 | Host-boot test for a wildcard admin list | **skipped:** the overlap host test already proves `Validate` is wired, and the wildcard branch has a unit test |
| pr-test | 2 | Direct `useApiAuthBridge` test | **fixed** together with the guard (F1) |
| silent-failure | Medium | The CORS startup warning uses a private console `LoggerFactory` instead of `app.Logger` | **skipped:** pre-existing tenant pattern, out of scope |
| silent-failure | Low | The retry panel gives no hint about the default API base URL; no Development-mode CORS debug log | **skipped:** covered by TD-016 / dev only |

Fix commit and scoped re-review: see `dod.md`, gate 7.

**Correction 2026-09-28 (gate 8 deep-review, SF1):** the scoped re-review missed that the rejection path cannot occur with the installed library. The test mocked a rejecting `signinRedirect`, which the real library never produces. It was re-fixed at gate 8 against the real `auth.error` contract.
