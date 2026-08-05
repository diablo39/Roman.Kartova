# EF Core patterns — Query patterns {#queries}

Section of `knowledge/csharp/efcore.md`.


- Project with `Select` to a DTO for read paths; `AsNoTracking()` when entities are read-only.
  Tracking every row of a large result is the most common hidden cost.
- Filter and page in the database. `ToListAsync()` before `Where`/`Skip` pulls the table into memory.
- `TagWith("checkout-cart-load")` stamps the generated SQL so a slow query in the database log maps
  back to code.
- Collection parameters (`Where(b => ids.Contains(b.Id))`) translate to individual scalar
  parameters with padding on the current LTS — plan-cache-friendly by default. Only override the
  translation mode after a measured plan problem.

### Split queries and cartesian explosion {#split-queries}

`Include` of multiple collections in one query multiplies rows (cartesian explosion). Use
`AsSplitQuery()` for multi-collection loads, or better, project exactly the shape you need:

```csharp
var order = await db.Orders
    .Where(o => o.Id == id)
    .Include(o => o.Lines)
    .Include(o => o.Payments)
    .AsSplitQuery()                 // two queries instead of Lines × Payments rows
    .SingleAsync(ct);
```

Split queries trade one round trip for consistency: the queries run separately, so pair them with
a transaction or accept the (usually fine) snapshot skew. A global default
(`UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)`) fits collection-heavy models;
per-query choice fits mixed ones.

### Compiled queries {#compiled-queries}

For a hot query executed thousands of times per second, `EF.CompileAsyncQuery` caches the
query-to-SQL translation once:

```csharp
private static readonly Func<AppDb, long, CancellationToken, Task<Order?>> GetOrder =
    EF.CompileAsyncQuery((AppDb db, long id, CancellationToken ct) =>
        db.Orders.AsNoTracking().SingleOrDefault(o => o.Id == id));
```

EF already caches compiled plans keyed by query shape, so compiled queries help only when the
per-execution cache lookup itself shows up in a profile — adopt on evidence, not by default.
