# EF Core patterns — Bulk updates and deletes {#execute-update}

Section of `knowledge/csharp/efcore.md`.


`ExecuteUpdateAsync`/`ExecuteDeleteAsync` run one set-based SQL statement without loading or
tracking entities — the right tool for "flag all expired rows" work:

```csharp
var stale = await db.Sessions
    .Where(s => s.LastSeen < cutoff)
    .ExecuteDeleteAsync(ct);
```

Semantics to design around:

- They bypass the change tracker: tracked entities in the same context are not updated, and
  `SaveChanges` interceptors and automatic concurrency checks do not run.
- They execute immediately (no batching with `SaveChanges`) and return the affected-row count —
  assert it when the count is a correctness signal.
- The current LTS accepts a regular lambda for the setters, so conditional updates compose without
  hand-built expression trees, and setters can target properties inside JSON-mapped complex types.
- Concurrency control is manual: include the token in the `Where` and treat zero affected rows as
  a conflict (next section).
