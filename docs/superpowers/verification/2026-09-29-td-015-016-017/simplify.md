# Gate 5 — /simplify (advisory) — branch f0a62b1..65d0c10

4 agents (reuse, simplification, efficiency, altitude), code-only diff (docs + openapi snapshot excluded). Every finding vetted; none auto-applied.

| # | Angle | Finding | Verdict |
|---|---|---|---|
| S1 | simplification | Dead usings in `src/Kartova.Api/Program.cs` (`System.Reflection`, `Microsoft.AspNetCore.Diagnostics.HealthChecks`, `Kartova.SharedKernel.AspNetCore.HealthChecks`) left after the move to SystemEndpoints | **apply** — residue of this diff |
| S2 | altitude | `useApiAuthBridge` — mark + `signinRedirect({returnTo})` sequence duplicated in the 401 handler and `retry` (guard set separately) | **apply** — one internal `reauthenticate()` used by both |
| S3 | simplification | Admin providers test lacks the Try-again assertion the tenant test has (coverage asymmetry) | **apply** — add assertion |
| S4 | reuse | `EndpointRouteRules.FindRepoFile` duplicates `KartovaPermissionsRules.FindRepoFile` | skip — sharing couples two unrelated rule classes; a shared test-paths helper is an out-of-scope refactor (reject-by-default) |
| S5 | simplification + altitude | `/config.js` nginx location block duplicated in both templates → `include` snippet | skip — adds a build input (extra COPY + include path) for a 5-line block; both templates are already per-app by design (ADR-0118 own CSP) and the CI check covers both |
| S6 | simplification | `ApiAuthBridge` / `AdminApiAuthBridge` near-identical → shared `ApiAuthGate` | skip — 3-line bodies binding app-specific setters; extraction is the reject-by-default "shared helper" class |
| S7 | efficiency | `clearReauthMarker()` sessionStorage write on every authed response → in-memory mirror | skip — `removeItem` on an absent key is µs; a mirror desyncs across full-page reloads (storage persists, mirror resets) → would stop clearing a live marker (behaviour risk) |
| S8 | efficiency | `<script src="/config.js">` render-blocking → `defer` | skip — tiny same-origin script before an empty `#root`; preload scanner still fetches the module in parallel; `defer` would change the asserted tag in the CI check for negligible gain |
| S9 | efficiency | CI runs the two runtime-config checks sequentially | skip — seconds of wall clock; parallel docker runs complicate failure output |
| S10 | altitude | `OpsAdminRoutes` carve-out → move `/health/detailed` under `/api/v1/admin/` | skip — rejected in brainstorming (ADR-0060 URL contract; spec Scope) |
| S11 | altitude | Program.cs regex source guard is heuristic → structural guard | skip — accepted trade-off in spec D4; structural variant needs the real host (rejected alternative) |
