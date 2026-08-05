# EF Core patterns — Interceptors {#interceptors}

Section of `knowledge/csharp/efcore.md`.


Interceptors hook EF pipeline events for cross-cutting concerns without polluting call sites.
Register on the context options (`options.AddInterceptors(...)`); keep them stateless and fast —
they run on every operation.

| Interceptor | Use for |
|---|---|
| `ISaveChangesInterceptor` | Audit fields (created/modified by/at), domain-event dispatch, soft-delete rewriting |
| `DbCommandInterceptor` | Per-request query counting (N+1 guard), slow-query logging, hint injection |
| `IDbConnectionInterceptor` | Token-based auth to the database (short-lived credentials) |
| `IMaterializationInterceptor` | Post-load initialization of entities |

Interceptors do not fire for `ExecuteUpdate`/`ExecuteDelete` SaveChanges hooks — audit logic that
must also cover bulk operations belongs in the database (triggers) or in the calling service.
