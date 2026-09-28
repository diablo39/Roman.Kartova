# Gate 5 — `/simplify` (advisory) — S2 web-admin

**Scope:** `git diff 2c03a2b...HEAD` (code only; docs excluded) at `ea427a3`. There were 4 parallel review angles: reuse, simplification, efficiency and altitude. Each finding was vetted by the author before being applied (CLAUDE.md gate 5).

## Applied

| # | Angle | Finding | Change |
|---|---|---|---|
| A | reuse + simplification | `AdminApiAuthBridge` (`web/src/admin/providers.tsx`) is a line-for-line copy of the tenant `ApiAuthBridge` (PR #47 live-ref race fix), so the fix would have to be kept in sync in two places | A shared `useApiAuthBridge(setTokenProvider, setUnauthorizedHandler)` hook in `@/shared/oidc/`. Both bridges become thin wrappers. |
| C | reuse + simplification | The centered message card (`flex h-full items-center justify-center` → `max-w-md space-y-3 text-center` → h1 + p [+ button]) is repeated in 3 new admin surfaces and 2 existing tenant ones | A shared `CenteredMessage({ heading, body, action? })` in `components/layout/`. The 3 admin surfaces and the tenant `NoAccessPage` / `PermissionsErrorShell` use it, with DOM unchanged. |
| D | simplification | `Program.cs` repeats the `AddPolicy(name, origins → WithOrigins…)` block for both CORS policies | Local helper `AddOriginPolicy(options, name, origins)` |
| G | altitude | The Dockerfile switches to `USER root` to `rm -rf` nginx's stock html dir before COPY. That is a symptom patch: every stock file the base image adds later needs the same cleanup. | Copy the build to a directory the base image never populates (`/usr/share/nginx/app`), point `root` there in both conf templates, and drop the root toggle and the `rm` |
| J | reuse | `statusOf()` (`web/src/admin/api/useAdminSession.ts`) re-reads the `__status` envelope, which `shared/api/openapi-fetch-helpers.ts` documents as "defined once" | Move `statusOf` into `openapi-fetch-helpers.ts` and export it from there; admin imports it |

## Skipped (with reason)

| Angle | Finding | Reason |
|---|---|---|
| simplification / reuse | Token-slot boilerplate (`let tokenProvider` + setters) is duplicated in `admin/api/client.ts` and `features/catalog/api/client.ts` | About 10 lines. The tenant module's exported names have 55 importers and spy-based tests; a factory buys little. |
| simplification / efficiency | `CorsOriginLists.Validate` normalizes inputs that `Program.cs` already normalized | Deliberate: `Validate` is a public guard that tests call with raw config (trailing slash, case). Normalizing twice is idempotent and costs microseconds once at startup. |
| simplification | `vite.admin.config.ts` repeats `plugins`/`alias` from `vite.config.ts` | About 6 lines. A base-config module adds indirection for two small configs. |
| altitude | Replace the custom dev HTML fallback with a multi-page config or a separate Vite root | A multi-page build would put admin into the tenant `dist` and break the separate-bundle rule (ADR-0118). A separate root restructures the second-entry design chosen in brainstorming. The fallback is dev/preview only and unit-tested. |
| altitude | Merge `admin.conf.template` and `default.conf.template` into one template with the CSP injected per app | A separate CSP per app is an explicit spec/ADR-0118 decision. Injecting the whole CSP at runtime would make it environment-configurable, which is weaker. |
| efficiency | `prebuild:admin` runs codegen again after the CI `codegen` step | Required: `src/generated/` is gitignored, and the admin Docker build runs only `npm run build:admin`, so it needs its own codegen hook. |
| efficiency | New arch tests rebuild the endpoint host per test method | Pre-existing pattern in `EndpointRouteRules`; changing it is out of scope. |
| efficiency | New realm seed tests re-parse the realm JSON per test | Pre-existing pattern in `KeycloakPlatformRealmSeedRules`; out of scope, and the cost is negligible. |
