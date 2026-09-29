# Gate 9: live verification on the running stack (2026-09-29, HEAD 552e474, images built at e8213be code)

- Stack: `docker compose up -d --build`.
- The SPA containers were then recreated with a **compose override** carrying a non-default runtime value: `KARTOVA_API_BASE_URL=http://127.0.0.1:8080` plus the matching `CSP_EXTRA_ORIGINS`.
- **Same images, no rebuild.** This proves the value comes from `/config.js` at container start, not from the `VITE_*` or built-in fallback.
- Browser: Playwright MCP, navigating in-SPA (ADR-0084).

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | `GET :4173/config.js` and `:4174/config.js` | Env values rendered, including `"apiBaseUrl":"http://127.0.0.1:8080"`. Headers `Content-Type: application/javascript` and `Cache-Control: no-store`. | `gate9-api.txt` |
| 2 | Tenant SPA sign-in (`admin@orga`) | Lands on `/catalog/applications`. **Every API call goes to `http://127.0.0.1:8080`** (auth/session, permissions, teams, systems, applications all 200), so the runtime value is used. Console: no errors from this change; one `vite.svg` favicon 404 predates it. | `gate9-tenant-runtime-config.png` |
| 3 | Tenant TD-017: context route answers 401 on every **authed** API call | **1** SSO round-trip and **2** rejected calls, then the "Signed in, but the session was rejected" panel with Try again / Sign out. No loop. Console logs `Re-authentication loop stopped…`. The panel is centred. | `gate9-tenant-reauth-panel.png` |
| 4 | Tenant: remove the route, click **Try again** | Re-auth, back in the app (`/catalog/applications`). `resolveReturnTo` correctly ignored the `/login-error` returnTo and logged a warning. | — |
| 5 | Admin console sign-in (`platform-admin@kartova.local`) | "Platform Admin" landing page. `admin/session/me` goes to `http://127.0.0.1:8080`. | `gate9-admin-runtime-config.png` |
| 6 | Admin TD-017: persistent authed 401 | **1** SSO round-trip and **2** rejected calls, then the panel. Try again after removing the route returns to the landing page. | `gate9-admin-reauth-panel.png` |
| 7 | TD-015 `/health/detailed` | Tenant-realm token **401**. Anonymous **401**. Operator token **200** `Healthy` (self, postgres, keycloak, migrations). | this file |
| 8 | TD-015 other system routes | `/health/live` 200 anonymous. `/api/v1/version` 200 anonymous. `/openapi/v1.json` 200: the `/api/v1/version` operation has `operationId: GetVersion` and tag `SystemEndpoints`, matching the regenerated snapshot. | this file |

**Observation (no action):** in scenario 3 the URL settled on `/login-error`. The callback's session POST was also forced to 401, so the callback routed to the login-error page, and the bridge panel rendered over it. Try again recovers to the app, because `resolveReturnTo` rejects that path.

After verification, `web` and `web-admin` were recreated with the default compose env.
