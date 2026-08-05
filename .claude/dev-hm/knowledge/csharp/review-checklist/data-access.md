# C# review checklist — Data access {#data-access}

Section of `knowledge/csharp/review-checklist.md`.


| Check | Severity | Notes |
|---|---|---|
| Parameterized queries | S0 | LINQ or `FromSqlInterpolated`; never concatenate input into SQL. Oracle SEC-CS-001 |
| No N+1 | S2 | Related data via `Include`/projection/split query, not per-row lazy loads in a loop |
| `AsNoTracking` for reads | S3 | Read-only queries do not need change tracking |
| Project to DTOs | S2 | `Select` to needed columns; do not materialize whole entities to map a few fields |
| Transactions around multi-write units | S1 | Multi-statement writes are atomic; use the execution-strategy retry for transient faults |
| Migrations reviewed | S2 | Schema changes are backward-compatible for rolling deploys; see `knowledge/postgresql/schema-design.md` |
