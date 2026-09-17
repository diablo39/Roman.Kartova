# DoD Ledger — Web CSP (Report-Only) (E-01.F-04.S-06b)

**Slice:** `2026-09-17-web-csp-report-only` · **Branch:** `master` · **HEAD:** `<pending commit>`
**PR:** direct-to-local-master (owner workflow) · **Last updated:** 2026-09-17
**Spec:** none — bounded slice (in-chat design, brainstorming bounded path); ADR-0116 governs
**Plan:** none — bounded slice

> DoD from `CLAUDE.md`. Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A · WAIVER.

## Scope

ADR-0116 interim hardening (sibling of S-06a): add a Content-Security-Policy to the web/nginx container to shrink the XSS surface behind the SPA's `sessionStorage` tokens. **Ships as `Content-Security-Policy-Report-Only`** — browser reports violations without blocking, so the **enforce-flip** (Report-Only → `Content-Security-Policy`) follows a real-browser gate-9 pass with zero risk. XSS-critical directive = strict `script-src 'self'` (no unsafe-inline/eval); `style-src` carries `'unsafe-inline'` (Scalar/react-aria inject inline `<style>`).

**Diff:** `web/nginx.conf` → `web/default.conf.template` (envsubst template + CSP header) · `web/Dockerfile` (COPY → `/etc/nginx/templates/`) · `docker-compose.yml` (web `CSP_EXTRA_ORIGINS`) · `tests/Kartova.ArchitectureTests/WebSecurityHeaderRules.cs` (+3 drift-sentinel tests). No app/backend code.

**helm:** N/A — chart has no web deployment (only api + migrator); nothing to wire.

**Follow-up (owner):** after browser gate-9 confirms zero CSP violations, flip Report-Only → enforcing (one-line header rename) + set `CSP_EXTRA_ORIGINS` for the real prod/staging origins.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-17 |
| 2 Per-task subagent review | ✅ PASS (2 fixes applied) | 2026-09-17 |
| 3 Full suite (arch; real-seam N/A) | ✅ PASS | 2026-09-17 |
| 4 Container build (images) | ✅ PASS | 2026-09-17 |
| 5 `/simplify` | N/A | 2026-09-17 |
| 6 `requesting-code-review` | WAIVER (owner, 2026-09-17) | 2026-09-17 |
| 7 `review-pr` | WAIVER (owner, 2026-09-17) | 2026-09-17 |
| 8 `deep-review` | ✅ PASS | 2026-09-17 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-09-17 |
| 9 Visual (browser, policy + enforce-flip) | ⏳ PENDING (owner) | — |
| 10 CI green on PR | ⏳ PENDING (owner) | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS — `dotnet build Kartova.slnx -c Debug -p:TreatWarningsAsErrors=true` → `0 Warning(s) 0 Error(s)`.

### 2 — Per-task subagent review
**Status:** ✅ PASS — `pr-review-toolkit:code-reviewer` on the full diff. No Critical. Two Important findings **fixed in this slice** (both were enforce-time breaks Report-Only would have surfaced):
1. `img-src` lacked `blob:` → `LogoUploader` object-URL preview would be blocked → added `blob:` (+ guarding assertion in `WebSecurityHeaderRules`).
2. Undefined `CSP_EXTRA_ORIGINS` leaks a literal `${...}` token → added defined-empty `ENV CSP_EXTRA_ORIGINS=""` in the Dockerfile (re-verified: no-env container now renders `connect-src 'self' ;`, no leak).
Comment-accuracy nit fixed (react-aria, not Scalar, justifies `style-src 'unsafe-inline'`). Reviewer confirmed correct: CSP-on-document-only, `$uri` envsubst scoping, Dockerfile/compose wiring, arch-test guard, no other breaking directive (no web workers / external fonts / inline scripts). **Carried to enforce-flip follow-up:** any future web k8s/helm manifest MUST set `CSP_EXTRA_ORIGINS` (helm has no web deployment today → N/A now); `frame-src`/`form-action` could narrow to KeyCloak-only (optional polish).

### 3 — Full test suite
**Status:** ✅ PASS — arch 74/74 (+3 new `WebSecurityHeaderRules`; RED before template, GREEN after — TDD). **Real-seam N/A:** slice wires no app HTTP/auth/DB/middleware — CSP is an nginx response header. The seam analog (container HTTP behavior) is verified in gate 4.

### 4 — Container build (images CI job)
**Status:** ✅ PASS — `docker build web` succeeded; container run with `CSP_EXTRA_ORIGINS` → `GET /` returns the `Content-Security-Policy-Report-Only` header with both origins substituted; `${CSP_EXTRA_ORIGINS}` filled, nginx `$uri` preserved; `nginx -t` = syntax OK. Header wiring proven at HTTP level (browser policy-behavior = gate 9).

### 5 — `/simplify`
**Status:** N/A — no business logic; diff = declarative nginx template + build/compose wiring + assertion-only arch test.

### 6 — `requesting-code-review`
**Status:** WAIVER (owner, 2026-09-17) — infra/config slice; gate 2 + gate 8 lensed the full diff (gate 2 surfaced + fixed 2 real items). Waiver, not green.

### 7 — `review-pr`
**Status:** WAIVER (owner, 2026-09-17) — standing set has little surface on nginx/docker/config + declarative test. Waiver, not green.

### 8 — `deep-review`
**Status:** ✅ PASS — reviewed against ADR-0116 intent. No Blocking/Should-fix. Confirmed: (a) Report-Only = non-breaking first step (correct given no browser verification this session); (b) CSP set only on the HTML document response (`location /`) — sub-resource CSP headers don't affect page policy, so the asset-location omission is correct; (c) `script-src 'self'` strict (XSS-critical), `style-src 'unsafe-inline'` scoped + justified (Scalar/react-aria); (d) origins env-injected → portable across environments. Note carried to follow-up: verify no `worker-src`/`unsafe-eval` need surfaces in the browser before the enforce-flip.

### Terminal re-verify (build + full suite)
**Status:** ✅ PASS — no code-mutating gate applied changes; build + arch green on final diff.

### 9 — Visual / API verification (running system)
**Status:** ⏳ PENDING (owner) — browser MCP unavailable this session. Load the app via the web container (`:4173`), exercise API calls + spec-render (Scalar) + OIDC silent-renew, and confirm **zero CSP violations** in the devtools console. On a clean pass, do the enforce-flip (Report-Only → `Content-Security-Policy`). HTTP-level header correctness already proven in gate 4.

### 10 — CI green on PR (`ci-local.sh` = pre-push mirror)
**Status:** ⏳ PENDING (owner) — direct-to-local-master; `images` CI job builds the web image if pushed.
