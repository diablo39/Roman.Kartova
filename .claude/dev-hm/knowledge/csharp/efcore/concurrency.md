# EF Core patterns — Optimistic concurrency {#concurrency}

Section of `knowledge/csharp/efcore.md`.


Default choice: a database-generated row version (`IsRowVersion()` /
`[Timestamp]`) — the database maintains it, every update is protected, nothing to remember in
application code. An application-managed token (`IsConcurrencyToken()` on a GUID you regenerate on
each update) is for databases without a rowversion type; it fails silently if any code path forgets
to bump it.

- `SaveChangesAsync` adds the token to the `UPDATE ... WHERE`; a stale token throws
  `DbUpdateConcurrencyException`. Handle it at the unit-of-work boundary: reload, re-apply or
  surface a conflict to the caller (HTTP 409/412 with the new state). Never catch-and-retry blindly
  — that reintroduces lost updates.
- With `ExecuteUpdateAsync`, enforce the token yourself:

```csharp
var updated = await db.Orders
    .Where(o => o.Id == id && o.Version == expectedVersion)
    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled), ct);
if (updated == 0) { /* concurrency conflict — reload and decide */ }
```

Optimistic concurrency is the default for web workloads; pessimistic locking (explicit
`FOR UPDATE` via raw SQL) is for short, high-contention critical sections and needs the database
file's locking guidance (`knowledge/postgresql/query-optimization.md`).
