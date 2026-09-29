# Gate 9 — Visual / live verification (ADR-0084, Playwright MCP) — 2026-09-29

**Build:** HEAD `c3800f6`, compose stack up (postgres, keycloak, migrator, api, web, web-admin). The admin Vite dev server was **cold-started** on :5174, with `node_modules/.vite` removed first.

| # | Step | Observed | Evidence |
|---|---|---|---|
| 1 | Cold entry `http://localhost:5174/` | Redirected to `localhost:8180/realms/kartova-platform/.../auth` with `client_id=kartova-admin-web`, `redirect_uri=http://localhost:5174/callback`, `code_challenge_method=S256` | snapshot |
| 2 | Sign in as `platform-admin@kartova.local` | Back on `/`, master shell: sidebar "Kartova" + `Overview` (active) + `Organizations` (disabled, "Coming soon"); top bar badge "PLATFORM ADMIN" + avatar "PA"; landing h1 "Platform Admin", "Signed in as Platform Admin · platform-admin@kartova.local", "Organization management is coming." | `gate9-admin-landing.png` |
| 3 | API | `GET http://localhost:8080/api/v1/admin/session/me` → **200**; console: **0 errors, 0 warnings** | network/console |
| 4 | User menu | Shows display name + email + "Sign out" | snapshot |
| 5 | Sign out | KeyCloak session ended → back to the kartova-platform sign-in page | snapshot |
| 6 | Sign in as `operator-norole@kartova.local` | "No access" / "This account is not a platform operator." / [Sign out], rendered without the shell. `session/me` → **403**, called exactly once (no retry). The only console entry is the browser's resource-load log for that 403. | `gate9-admin-no-access.png` |
| 7 | Production container `http://localhost:4174/some/deep/link` (nginx, enforcing admin CSP) | SSO → callback → the deep link falls through the catch-all to `/`, so it shows No access for the no-role user. **0 CSP violations** (no "Refused to …" entries); the only entry is the expected 403 log. | console |

Tenant regression is covered by the full E2E run on the same stack: 11/11 passed (`terminal-reverify.txt`).
