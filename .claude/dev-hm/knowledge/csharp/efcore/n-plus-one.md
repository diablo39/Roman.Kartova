# EF Core patterns — N+1: detection and fixes {#n-plus-one}

Section of `knowledge/csharp/efcore.md`.


The failure mode: load a list, then touch a navigation per element — one query becomes N+1.
Lazy loading proxies turn this on silently; do not enable `UseLazyLoadingProxies` in services.

Detect:

- Turn the lazy-load warning into an error in development:
  `optionsBuilder.ConfigureWarnings(w => w.Throw(CoreEventId.LazyLoadOnDisposedContextWarning, CoreEventId.NavigationLazyLoading))`.
- Log generated SQL in development (`LogTo`/`EnableSensitiveDataLogging` locally only) or attach a
  `DbCommandInterceptor` that counts commands per request and flags outliers.
- In integration tests, assert command counts for list endpoints (capture via interceptor); a
  regression from 2 to N+2 queries is a failed test, not a production incident.

Fix by loading the related data with the parent: `Include`/`ThenInclude`, a projection that pulls
the needed columns in one query, or an explicit second query keyed by the collected IDs.
