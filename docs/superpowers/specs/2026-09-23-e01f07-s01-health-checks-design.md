# E-01.F-07.S-01 — Health Check Endpoints — Design

**Story:** E-01.F-07.S-01 (`docs/product/phases/phase-0-foundation.md`)
**ADR:** [ADR-0060](../../architecture/decisions/ADR-0060-three-probe-health-checks-aspnet-core-framework.md) (authoritative on the *what*; this doc is the *how* for this repo)
**Date:** 2026-09-23
**Status:** draft

## Context

`Kartova.Api` already maps `/health/live`, `/health/ready`, `/health/startup` (`src/Kartova.Api/Program.cs`) with two checks (`self`, `postgres`), but:
- `/health/startup` filters on the `"ready"` tag, not `"startup"` — bug, the endpoint currently returns the readiness set.
- No migrations check, no KeyCloak check.
- No `/health/detailed`.
- The default `MapHealthChecks` response writer emits a plain-text status string, not the JSON shape ADR-0060 specifies (`status`/`totalDuration`/`entries[]`).
- Kafka/Elasticsearch/MinIO have no client integration anywhere in the repo yet — out of scope here, tracked as [TD-011](../../engineering/tech-debt.md#td-011--kafkaelasticsearchminio-health-checks-not-yet-registered).

Single ASP.NET Core host today (`Kartova.Api`); `Kartova.Migrator` is a separate one-shot console app, not a long-running service, so it does not need probes.

## Scope

**In:** Postgres check (existing, fix tags), KeyCloak check (new), migrations-pending check (new, all modules), fix `/health/startup` tag filter, JSON response writer (compact + detailed), `/health/detailed` gated to `PlatformAdmin`.

**Out (TD-011):** Kafka, Elasticsearch, MinIO checks — no client to check yet.

## Components

All in `Kartova.Api` except the response writer, which goes in `Kartova.SharedKernel.AspNetCore` (generic ASP.NET Core infra, no module knowledge — ADR-0060 notes future reuse by a status-page service; the writer is the one piece with no Kartova.Api-specific dependency, so it costs nothing to place there now rather than move it later).

| File | Responsibility |
|---|---|
| `src/Kartova.Api/HealthChecks/KeycloakHealthCheck.cs` | `IHealthCheck`. HTTP GET `{BaseUrl}/realms/{Realm}/.well-known/openid-configuration` via a named `HttpClient` (`AddHttpClient<KeycloakHealthCheck>`), reusing `KeycloakAdminOptions` (`Kartova.SharedKernel.Identity`) for `BaseUrl`/`Realm`. Catches transport exceptions internally → `Unhealthy(ex.Message, ex)`, never throws. Tags `["ready","startup"]`. |
| `src/Kartova.Api/HealthChecks/ModuleMigrationsHealthCheck.cs` | `IHealthCheck`. Constructor takes `IServiceProvider` + the `IModule[]` array (registered as a singleton array via DI, same array Program.cs already builds). For each module, opens a DI scope, resolves `(DbContext)scope.ServiceProvider.GetRequiredService(module.DbContextType)` (mirrors `Kartova.Migrator/Program.cs`), calls `GetPendingMigrationsAsync`. Unhealthy (listing the offending module names) if any module has pending migrations. Tag `["startup"]` only. |
| `src/Kartova.SharedKernel.AspNetCore/HealthChecks/HealthCheckJsonResponseWriter.cs` | Static `Task WriteCompact(HttpContext, HealthReport)` and `Task WriteDetailed(HttpContext, HealthReport)`. Compact: `status`, `totalDuration`, `entries[].{status, duration, tags, description?}`. Detailed adds `entries[].exception?` (message only, no stack trace) and echoes the check's registered name as-is (already non-sensitive — "postgres", "keycloak", "migrations"). Both set `Content-Type: application/json`; HTTP status via existing `HealthCheckOptions.ResultStatusCodes` (unchanged: Healthy/Degraded→200, Unhealthy→503). |
| `src/Kartova.Api/Program.cs` | Wire it all: fix `postgres` tags → `["ready","startup"]`; register `KeycloakHealthCheck` + `ModuleMigrationsHealthCheck`; fix `/health/startup` predicate → `Tags.Contains("startup")`; set `ResponseWriter = HealthCheckJsonResponseWriter.WriteCompact` on all three public maps; add `/health/detailed` (`Predicate = _ => true`, `ResponseWriter = WriteDetailed`, `.RequireAuthorization(p => p.RequireRole(KartovaRoles.PlatformAdmin))` — same gate as `ModuleRouteExtensions.cs:34`). |

No new project, no new NuGet package (KeyCloak check is custom — no `AspNetCore.HealthChecks.Keycloak` package is pinned or verified to exist in `Directory.Packages.props`; a hand-written `IHealthCheck` avoids depending on an unverified package).

## Data flow

kubelet / ops client → `GET /health/{live,ready,startup,detailed}` → ASP.NET Core `HealthCheckMiddleware` runs the tag-filtered subset of registered `IHealthCheck`s in parallel → `HealthReport` → response writer serializes → status code per `Healthy`(200)/`Degraded`(200)/`Unhealthy`(503).

## Error handling

- Each custom `IHealthCheck` catches its own exceptions and returns `Unhealthy(description, exception)` — the framework's own per-check timeout (`HealthCheckOptions` default) still applies as a backstop.
- A check throwing uncaught is a bug in that check, not expected behavior — no global try/catch added around the framework's own dispatch (it already isolates per-check failures into an `Unhealthy` result).
- `/health/detailed` reached without `PlatformAdmin` → existing auth pipeline returns 401 (anonymous) / 403 (wrong role), consistent with every other `RequireRole(PlatformAdmin)` route.

## Testing (per docs/TESTING-STRATEGY.md)

Wiring slice (HTTP + auth + DB + a new external call) → real-seam integration tests, `Kartova.Api.IntegrationTests`, using the existing `KeycloakAndPostgresContainers` aggregate fixture (Level 2 — this slice exercises KeyCloak itself, not just JWT validation).

- `/health/live` — 200, `entries` contains only `self`; killing the Postgres container does not affect it (proves live excludes deps).
- `/health/ready` — 200 Healthy when Postgres+KeyCloak are up; 503 when Postgres container is stopped mid-test.
- `/health/startup` — includes `migrations` entry; Healthy when the fixture's `RunModuleMigrationsAsync` has already applied migrations (the normal fixture state).
- `/health/detailed` — 401 anonymous; 403 with a non-PlatformAdmin authenticated client; 200 with a PlatformAdmin client, body contains `exception`/full detail fields absent from the compact writer's output on the same fixture state.
- `KeycloakHealthCheck` unit test (NSubstitute'd `HttpMessageHandler` or a fake base address) — Healthy on 200 discovery response, Unhealthy on non-200 and on transport exception.
- `ModuleMigrationsHealthCheck` — covered at integration tier only (needs the real `IModule[]`/DbContext wiring); no meaningful unit-tier isolation without re-mocking the whole module array.

No Dockerfile/`COPY`/restore-surface change → gate 4 (`images` CI job) is N/A for this slice.

## Impact Analysis (LSP)

New symbols only (`KeycloakHealthCheck`, `ModuleMigrationsHealthCheck`, `HealthCheckJsonResponseWriter`) — no existing C# symbol's signature or behavior changes. `Program.cs` edits are additive/config (tag strings, health-check registration, endpoint mapping) — not a call-site change to any existing method. N/A — no `LSP` blast-radius query needed.

## Out of scope / follow-ups

- Kafka/Elasticsearch/MinIO checks — TD-011.
- Alerting (E-01.F-07.S-04) — separate story.
- Structured logging (S-02) and metrics (S-03) — separate stories, separate specs.
