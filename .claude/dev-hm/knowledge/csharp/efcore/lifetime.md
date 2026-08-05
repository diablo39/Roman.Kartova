# EF Core patterns — Context lifetime and registration {#lifetime}

Section of `knowledge/csharp/efcore.md`.


- `AddDbContext<T>` registers the context `Scoped`: one instance per request/unit of work. A
  `DbContext` is not thread-safe — never share one across parallel tasks.
- Singletons and background services must not capture a scoped context. Inject
  `IDbContextFactory<T>` (`AddDbContextFactory`) and create a context per unit of work:

```csharp
await using var db = await dbFactory.CreateDbContextAsync(ct);
```

- `AddDbContextPool` reuses context instances to cut allocation on very hot request paths. It
  forbids per-instance state (no constructor-injected scoped services into the context, no
  `OnConfiguring` that varies per request). Measure before adopting; the default non-pooled scoped
  registration is correct for most services.
- Enable retrying execution strategies for cloud databases (`EnableRetryOnFailure` /
  provider equivalent). With a retrying strategy, user-initiated transactions must run inside
  `strategy.ExecuteAsync(...)` — a raw `BeginTransaction` around multiple `SaveChanges` calls is
  not retry-safe.
