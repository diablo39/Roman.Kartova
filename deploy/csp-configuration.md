# Configuring the Content-Security-Policy (CSP)

Operator guide for the web app's CSP. **Decision + rationale:** [ADR-0116](../architecture/decisions/ADR-0116-spa-holds-tokens-bff-deferred.md) (interim XSS hardening; story E-01.F-04.S-06b). This file is the *how-to*.

## Where CSP lives

- **Only the web container** (nginx image) sends CSP. Source: [`web/default.conf.template`](../../web/default.conf.template) — an envsubst template baked into the image (`web/Dockerfile` → `/etc/nginx/templates/`, rendered to `/etc/nginx/conf.d/default.conf` at container start).
- The header is set **only on the HTML document response** (`location /`). Sub-resource (JS/CSS) responses don't need it — a page's policy comes from the document response.
- The **vite dev server (`:5173`) sends no CSP** — nginx isn't in that path. CSP is a container/prod concern; verify it against the built image (compose `:4173`), not the dev server.
- Drift is guarded by arch tests: [`tests/Kartova.ArchitectureTests/WebSecurityHeaderRules.cs`](../../tests/Kartova.ArchitectureTests/WebSecurityHeaderRules.cs).

## Current state: ENFORCING

The header is **`Content-Security-Policy`** (enforcing) as of 2026-09-17. It shipped Report-Only, and was flipped after gate-9 (`e2e/csp-check.mjs`) confirmed 0 real violations under enforcement across every SPA surface (login, catalog lists/detail, the Scalar API Definition tab, logo upload, graph). Violations now **block**. To roll back to observe-only, rename the `add_header` in `web/default.conf.template` back to `Content-Security-Policy-Report-Only` (and revert the sentinel in `WebSecurityHeaderRules.cs`).

## 1. Set the per-environment origins (`CSP_EXTRA_ORIGINS`)

CSP must list every **cross-origin** the SPA reaches: the **API** and **KeyCloak**. These differ per environment, so they're injected at container start via the `CSP_EXTRA_ORIGINS` env var (space-separated, browser-facing origins — scheme+host+port, no path).

They flow into `connect-src` (XHR/fetch + OIDC token/userinfo/silent-renew), `frame-src` (KeyCloak silent-renew iframe), and `form-action` (login redirect).

| Environment | Set `CSP_EXTRA_ORIGINS` where | Example value |
|-------------|-------------------------------|---------------|
| Local compose | `docker-compose.yml` → `web.environment` (already set) | `http://localhost:8080 http://localhost:8180` |
| k8s / Helm | the web Deployment's env (⚠️ chart has **no web deployment yet** — add the var when it lands) | `https://api.example.com https://auth.example.com` |

These must match the SPA's build-time `VITE_API_BASE_URL` and `VITE_OIDC_AUTHORITY` (browser-facing origins).

**Footgun:** nginx envsubst only substitutes *defined* env vars and has no `${VAR:-default}` syntax. The Dockerfile defines `ENV CSP_EXTRA_ORIGINS=""` so an unset var renders cleanly (`connect-src 'self' ;`) instead of leaking a literal `${CSP_EXTRA_ORIGINS}`. **Always set the var explicitly in every deployment** — an empty/omitted var means the SPA can't reach the API or KeyCloak once enforced.

## 2. Enforce-flip (Report-Only → enforcing)

Do this only **after** a clean browser pass (step 4).

In `web/default.conf.template`, rename the header:

```
- add_header Content-Security-Policy-Report-Only "…" always;
+ add_header Content-Security-Policy "…" always;
```

Then update the arch-test constant in `WebSecurityHeaderRules.cs` (it asserts `Content-Security-Policy-Report-Only` — a deliberate tripwire so the flip can't happen silently). Rebuild the image.

## 3. Adding a new source (when the SPA legitimately needs one)

If a feature adds a script/style/font/image/connection to a new origin, the enforcing policy will block it. Update the matching directive in `web/default.conf.template`:

| Need | Directive | Notes |
|------|-----------|-------|
| Call a new backend origin | `connect-src` | prefer routing through the existing API |
| Load an external font | `font-src` | add the origin (e.g. Google Fonts) |
| Embed a new iframe | `frame-src` | |
| A new image source scheme | `img-src` | already allows `'self' data: blob:` (blob: = LogoUploader object-URL preview) |

**Never** add `'unsafe-inline'` or `'unsafe-eval'` to **`script-src`** — that's the XSS-critical directive and the whole point of the policy. (`style-src` carries `'unsafe-inline'` only because react-aria writes inline `style=` on positioned overlays; the arch test forbids weakening `script-src`.) After any change, update the arch tests if the guarded invariant moved.

## 4. Verify

- **HTTP level (any environment):**
  ```
  curl -sI http://localhost:4173/ | grep -i content-security-policy
  ```
  Confirm the header is present and `${CSP_EXTRA_ORIGINS}` is substituted (real origins, not a literal token).
- **Browser (the gate that authorizes the enforce-flip):** load the app via the container, open devtools → Console, and exercise the full surface — log in (OIDC redirect + silent-renew), list/detail pages, API-spec render (Scalar), logo upload preview. **Zero CSP violation reports** = safe to flip. Any violation names the blocked directive/source → fix per step 3 first.

## Current directives (reference)

```
default-src 'self';
script-src 'self';                         ← strict, XSS-critical (never weaken)
style-src 'self' 'unsafe-inline';          ← react-aria inline styles
img-src 'self' data: blob:;                ← blob: = LogoUploader preview
font-src 'self';
connect-src 'self' https://api.scalar.com ${CSP_EXTRA_ORIGINS};   ← API + KeyCloak + Scalar registry
frame-src ${CSP_EXTRA_ORIGINS};            ← KeyCloak silent-renew iframe
form-action 'self' ${CSP_EXTRA_ORIGINS};   ← login redirect
object-src 'none'; base-uri 'self'; frame-ancestors 'none';
```

No `report-uri`/`report-to` — Report-Only violations surface in the browser console only (sufficient for the interim gate-9 observation; add a collector later if server-side aggregation is wanted).

## Scalar (API Definition tab) — external origins

The Scalar spec renderer (`web/src/features/catalog/components/spec/SpecRender.tsx`) is the one component that reaches outside `'self'`. Gate-9 probing (`e2e/csp-check.mjs`) found three:

| Scalar behavior | Handling |
|-----------------|----------|
| Loads webfonts from `fonts.scalar.com` | **Suppressed** — `withDefaultFonts: false` (uses the system font stack). No `font-src` allowance needed. |
| Usage telemetry | **Suppressed** — `telemetry: false`. |
| "Curated registry" prefetch to `api.scalar.com` | **Allowlisted** in `connect-src` (`https://api.scalar.com`). Not config-suppressible without blanking `externalUrls.apiBaseUrl`, which breaks Scalar's own client; connect-only, so `script-src` stays strict. |

Re-run `e2e/csp-check.mjs` after any Scalar upgrade or config change — a new Scalar version may add or drop an external origin.

## Verifying enforce-readiness with `e2e/csp-check.mjs`

`e2e/csp-check.mjs` drives headless Chromium across the SPA surfaces and reports every CSP violation by directive and surface. **Caveat:** it runs under Playwright, which injects its own `eval` — so `script-src eval` reports from this probe are *harness noise, not the app* (the served bundle is `eval`-free; verify with `grep -rE 'eval\(|new Function\(' dist/assets`). Treat only non-`script-src`-eval violations as real. A run whose only violations are `script-src eval` = enforce-ready; confirm once in a normal (non-Playwright) browser's devtools before flipping.
