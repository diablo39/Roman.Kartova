# Untitled UI migration — cold-start Playwright evidence

Captured 2026-05-01 against `feat/untitled-ui-migration` branch (HEAD `b780440`+).
Stack: docker compose (api + postgres + keycloak + migrator) + cold Vite dev server.

## Bug found and fixed during this walkthrough

**`providers.tsx` — ThemeProvider crashed on blank `light` class token.**

`value={{ light: "", dark: "dark-mode" }}` caused `next-themes` to call
`classList.remove("")` which throws `SyntaxError: The token provided must not be empty.`
This crashed the entire React tree (blank white page) on cold load.

Fix: changed to `value={{ dark: "dark-mode" }}` (omit the `light` key entirely;
`next-themes` applies no class for the default/light theme when the key is absent).

Commit included in this branch alongside evidence.

## Flows verified

1. `login.png` — anonymous load; SPA redirects to KeyCloak login (`http://localhost:8180/realms/kartova/...`).
2. `catalog-list.png` — authenticated as admin@orga.kartova.local; catalog list page renders with Untitled UI primitives (sidebar with purple active state, top bar with ORG A selector and avatar, table with applications).
3. `register-dialog.png` — Register Application modal open with all three fields filled (Name: smoke-untitled, Display Name: Smoke Untitled, Description: Untitled UI verification). Modal backdrop blur visible. Labels rendered via Untitled `Input`'s `label` prop.
4. `application-detail.png` — detail page for the just-created application (`/catalog/applications/960daf02-018d-4f5c-b462-ba00adeb4b72`).

## Console

`console.log` is the concatenated console-message capture from all four flows. The two
pre-fix `SyntaxError` entries are from the initial cold load before the fix was applied.
Post-fix: 0 errors, 0 warnings across all four flows.
