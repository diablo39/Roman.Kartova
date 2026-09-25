# Modern .NET platform patterns — EF Core and data access

Section of `knowledge/csharp/platform.md`.


- One `DbContext` per unit of work, registered `Scoped`; do not share across threads.
- Parameterize every query. LINQ parameterizes automatically; for raw SQL use
  `FromSql`/`FromSqlInterpolated` (interpolated arguments become parameters), never string
  concatenation. See `knowledge/csharp/review-checklist/data-access.md`.
- Project to DTOs with `Select` to avoid over-fetching; use `AsNoTracking` for read-only queries.
- Avoid N+1: load related data with `Include`/split queries or explicit projection.
- Wrap multi-statement writes in a transaction or the built-in execution-strategy retry.

Depth — context lifetimes and pooling, split/compiled queries, `ExecuteUpdate`/`ExecuteDelete`,
concurrency tokens, interceptors, N+1 diagnostics, and rolling-deploy migrations — is in
`knowledge/csharp/efcore.md`.
