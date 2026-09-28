# Gate 6 — `requesting-code-review` (whole branch) — S2 web-admin

**Range:** `2c03a2b..5fdd094` (20 commits) · **Reviewer:** fresh subagent on the most capable model (superpowers `code-reviewer.md` template) · **Date:** 2026-09-28
**Verdict:** Ready to merge — **with fixes**. 0 Critical · 3 Important · 9 Minor.

## Strengths (summary)
- ADR-0118 isolation is enforced by tests, not only described:
  - the import-closure test is guarded against passing vacuously;
  - arch rules pin the per-route CORS policy in both directions;
  - the live-KeyCloak CORS matrix includes a real tenant-origin GET that carries a valid operator token.
- CORS design: endpoint `RequireCors` overrides the default; startup fails on overlapping or wildcard admin lists; origins are normalized and a trailing-slash config is tested.
- Access-check states: 403, 401 and 5xx/network are each tested, and the 401/403 tests assert the call count (no retries).
- Gate-5 G (a dedicated nginx root) was a real improvement for both images. The shared `useApiAuthBridge` keeps the PR #47 fix in one place.
- Helm stays backward compatible: the CORS env vars are rendered only when an origin is set, and the render check asserts both directions.

## Findings and disposition

| ID | Sev | Finding | Disposition |
|---|---|---|---|
| I1 | Important | The `web/Dockerfile` build stage declares no `ARG` for `VITE_*`, so the runbook's build-time config is silently dropped and every image points at localhost | fixed (gate-6 fix commit): ARGs added + README usage |
| I2 | Important | `deploy/README.md` and `deploy/csp-configuration.md` still say "no web Deployment". Both SPAs now default to `cspExtraOrigins: ""`, and with CSP enforcing the SPA cannot reach API/KeyCloak. | fixed: docs say `*.cspExtraOrigins` must be set |
| I3 | Important | Terminal re-verify must re-run the full `dotnet test` + `e2e/run.sh`: gate-5 d337205 touched the tenant nginx root, auth bridge, `NoAccessPage` and CORS code | process: done at terminal re-verify |
| M1 | Minor | A tenant `Cors:AllowedOrigins` of `*` lets the admin origin read tenant routes (breaks a spec-matrix row) | fixed: `Validate` rejects tenant `*` when the admin list is non-empty |
| M2 | Minor | Persistent 401 → endless `signinRedirect` loop (the tenant SPA has the same pattern) | TD-017 |
| M3 | Minor | A direct visit to `/callback` with no auth params hangs on "Completing sign-in…" | fixed: navigates to `/` |
| M4 | Minor | Top-bar menu shows blank name/email while the landing page shows "—" | fixed: shared empty→"—" helper |
| M5 | Minor | No test proves `Program.cs` calls `CorsOriginLists.Validate` | fixed: host-boot test with overlapping config |
| M6 | Minor | Empty admin-list warning is logged when the console is disabled | accepted: the warning is truthful |
| M7 | Minor | ADR index row says "own Helm release" | fixed: "own Deployment in the kartova chart" |
| M8 | Minor | `admin.html` favicon `/vite.svg` does not exist | fixed: `/favicon.svg` |
| M9 | Minor | Unrelated LSP/CLAUDE.md commits (`911a7b3`, `e3d6f1e`) are in the branch | called out in the PR description |

## Deferred-minor triage (from the SDD ledger)
All 20 lines were judged either ok-to-defer or agree-with-ruling. #14 and #16 were superseded by gate-5 G. On #15 (root-owned COPY'd files) the reviewer says it is not a defect: files that are root-owned, world-readable and immutable to uid 101 are the safer state. It is kept as is.

## Declined to judge (reviewer)
| Item | Reason |
|---|---|
| Helm `enabled: true` defaults for images that CI does not publish | spec choice |
| Preflight for an unmapped method falls back to the tenant policy | framework 405 behavior; no data exposure |
| Tenant dev server also serves `/admin.html` | KeyCloak rejects the redirect, admin CORS rejects the origin; dev only |
| Ingress/WAF 403 shown as "not an operator" | the spec defines 403 as no-access |
| CORS env vars leak across test classes | pre-existing pattern |
| "Loading…" text instead of a skeleton | cosmetic |
| `post.logout.redirect.uris` not asserted | not required by the spec |
| TD-015 and the operator-audit follow-up | out of scope |
