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
- Module-layering violations and mutation-score gaps — fitness tests handle

## Mediation & messaging
- Wolverine `IMessageBus` only. Flag `MediatR.IMediator`, `IRequestHandler<>`, `IPublisher`, `MassTransit.*`.
- Cross-module calls go through `IMessageBus` or Kafka events. Flag direct project references between modules.
- Outbound Kafka via Wolverine transactional outbox. Flag direct `IProducer` / raw Confluent client usage.
- Inbound Kafka via KafkaFlow. Flag in-process `IConsumer` patterns.
- Sync HTTP endpoints use direct handler dispatch (shared scope). Flag `IMessageBus.InvokeAsync` inside sync HTTP endpoints; `PublishAsync` stays allowed.

## Tenant scope & data access
- `ITenantScope` lifecycle is owned by `TenantScopeBeginMiddleware` + `TenantScopeCommitEndpointFilter`. Flag handlers calling `Begin`/`CommitAsync` or resolving `ITenantScope` directly.
- Register module DbContexts via `AddModuleDbContext<T>`. Flag raw `services.AddDbContext<T>` for tenant-owned entities.
- `AdminOrganizationDbContext` with `BYPASSRLS` is the only allowed RLS bypass — don't flag.
- App startup must not call `Database.Migrate()` or `EnsureCreated()`. Migrations run only via `Kartova.Migrator`.
- `SET LOCAL app.current_tenant_id` belongs in `TenantScopeBeginMiddleware`. Flag any other path setting tenant scope.

## HTTP & lists
- Routes register via `MapTenantScopedModule(slug)` / `MapAdminModule(slug)`. Flag raw `app.Map*("/api/...")` calls outside these helpers.
- Error responses use `application/problem+json` via `Results.Problem()` / `AddProblemDetails()`. Flag ad-hoc `{ "error": "..." }` shapes.
- New list endpoints accept `sortBy`, `sortOrder`, `cursor`, `limit` and return `CursorPage<T>`. Flag `IEnumerable<T>`/`List<T>` returns from list endpoints. `[BoundedListResult]` requires inline `// reason: ...`.

## Coverage exclusion
- `[ExcludeFromCodeCoverage]` is required on: types in `*.Contracts` assemblies; `*Dto`/`*Request`/`*Response`; `*Module.cs` composition roots; `IDesignTimeDbContextFactory<>` factories. Flag when missing on these; don't propose it elsewhere.

## Secrets, webhooks, observability
- Persist OAuth tokens / secrets only through the AES-256-GCM helper with per-tenant DEK. Flag new columns/properties holding raw token or secret values, plaintext persistence, or hand-rolled crypto.
- Webhook receivers use the shared retry+DLQ+idempotency+rate-limit+HMAC pipeline. Flag bespoke webhook receive code reimplementing any of these.
- Structured `ILogger` only. Flag `Console.Write*`, `Console.Error.*`, and `ILogger.LogXxx` calls in tenant-scoped paths missing `tenant_id` / `correlation_id` properties.

## ❌ / ✅ quick reference
| Concern    | ❌ Bad                          | ✅ Good                          |
|------------|----------------------------------|----------------------------------|
| Mediation  | `IMediator.Send` / `MassTransit` | `IMessageBus`                    |
| DbContext  | `services.AddDbContext<T>`       | `AddModuleDbContext<T>`          |
| List API   | `Task<List<X>>`                  | `Task<CursorPage<X>>`            |
| Errors     | `return BadRequest(new {error})` | `Results.Problem(...)`           |
| Routes     | `app.Map*("/api/x", ...)`        | `MapTenantScopedModule("x")`     |
| Tokens     | `entity.AccessToken = raw`       | `secretCipher.Protect(raw)`      |
