---
applyTo: "src/**/*.cs"
---

## Don't comment on
- Style, `var`, file-scoped namespaces, `using` order — `.editorconfig` handles
- Missing XML docs; `ConfigureAwait(false)` style; `record`->`class` swaps
- "Add interface for testability" when concrete is fine
- Cyclomatic complexity, function length — `/simplify` handles
- Proposing `[ExcludeFromCodeCoverage]` on production logic
- Handler placement in `*.Infrastructure` (allowed by design)
- `AdminOrganizationDbContext` `BYPASSRLS` (only allowed RLS bypass)
- Module-layering violations and mutation-score gaps — fitness tests handle

## Mediation & messaging
- Wolverine `IMessageBus` only. Flag `MediatR.IMediator`, `IRequestHandler<>`, `IPublisher`, `MassTransit.*`.
- Outbound Kafka via Wolverine outbox. Flag direct `IProducer` / raw Confluent client.
- Inbound Kafka via KafkaFlow. Flag in-process `IConsumer` patterns.
- Sync HTTP endpoints dispatch handlers directly. Flag `IMessageBus.InvokeAsync` in sync HTTP; `PublishAsync` allowed.

## Tenant scope & data access
- `ITenantScope` lifecycle is owned by `TenantScopeBeginMiddleware` + `TenantScopeCommitEndpointFilter`. Flag handlers calling `Begin`/`CommitAsync` or resolving `ITenantScope` directly.
- Register module DbContexts via `AddModuleDbContext<T>`. Flag raw `services.AddDbContext<T>` for tenant-owned entities.
- App startup must not call `Database.Migrate()` or `EnsureCreated()` — migrations only via `Kartova.Migrator`.
- `SET LOCAL app.current_tenant_id` only in `TenantScopeBeginMiddleware`. Flag any other path setting tenant scope.

## HTTP & lists
- Routes register via `MapTenantScopedModule(slug)` / `MapAdminModule(slug)`. Flag raw `app.Map*("/api/...")` calls outside these helpers.
- HTTP verbs: `PUT /{id}` for full replacement; `POST /{id}/<action>` for domain commands. PATCH banned. Flag `[HttpPatch]`, `MapPatch`, `Methods = ["PATCH"]`.
- Entities use `Guid` UUIDs only; URLs use `{id:guid}`. Flag new slug / kebab-`Name` properties, `{slug}` route segments, display-name uniqueness rules.
- Error responses use `application/problem+json` via `Results.Problem()` / `AddProblemDetails()`. Flag ad-hoc `{ "error": "..." }` shapes.
- New list endpoints accept `sortBy`, `sortOrder`, `cursor`, `limit` and return `CursorPage<T>`. Flag `IEnumerable<T>`/`List<T>` returns from list endpoints. `[BoundedListResult]` requires inline `// reason: ...`.

## Coverage exclusion
- `[ExcludeFromCodeCoverage]` is required on: types in `*.Contracts` assemblies; `*Dto`/`*Request`/`*Response`; `*Module.cs` composition roots; `IDesignTimeDbContextFactory<>` factories. Flag when missing on these; don't propose it elsewhere.

## Secrets, webhooks, observability
- Persist OAuth tokens / secrets only through the AES-256-GCM helper with per-tenant DEK. Flag new columns/properties holding raw secrets, plaintext persistence, or hand-rolled crypto.
- Webhook receivers use the shared retry+DLQ+idempotency+rate-limit+HMAC pipeline. Flag bespoke webhook receive code reimplementing any of these.
- Structured `ILogger` only. Flag `Console.Write*`/`Console.Error.*` and `ILogger.LogXxx` in tenant paths missing `tenant_id`/`correlation_id`.

## ❌ / ✅ quick reference
| Concern    | ❌ Bad                          | ✅ Good                          |
|------------|----------------------------------|----------------------------------|
| Mediation  | `IMediator.Send` / `MassTransit` | `IMessageBus`                    |
| DbContext  | `services.AddDbContext<T>`       | `AddModuleDbContext<T>`          |
| List API   | `Task<List<X>>`                  | `Task<CursorPage<X>>`            |
| Errors     | `return BadRequest(new {error})` | `Results.Problem(...)`           |
| Routes     | `app.Map*("/api/x", ...)`        | `MapTenantScopedModule("x")`     |
| Verbs      | `[HttpPatch]` / `MapPatch`       | `PUT /{id}` or `POST /{id}/<act>`|
| Route IDs  | `"/teams/{slug}"`                | `"/teams/{id:guid}"`             |
