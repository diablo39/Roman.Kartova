# Gate 6 — Mutation (Kartova.Catalog.Application, --since:master)

**Score: 89.74%** (target 80%) → PASS. 10 CompileError mutants excluded (record positional-param mutations that don't compile).

Run: `dotnet-stryker --since:master --project Kartova.Catalog.Application.csproj` (2026-07-09).

## Survivors (4) — all in PRE-EXISTING code, not this slice's derivation logic
The changed Application files are `DerivedDependencies.cs` (new — the derivation core) and `GraphTraversal.cs` (only the `GraphTraversalEdge` record gained a `Provenance` param). Because `--since` mutates the whole touched file, Stryker re-mutated `GraphTraversal.cs`'s pre-existing BFS direction logic:

- `GraphTraversal.cs:32` — Equality mutation (node-key tuple equality)
- `GraphTraversal.cs:45` — Conditional(true) on the `Outgoing` direction arm
- `GraphTraversal.cs:46` — Conditional(true) on the `Incoming` direction arm
- `GraphTraversal.cs:47` — Conditional(true) on the `All` direction arm

**DerivedDependencies.cs (this slice's new logic): 0 survivors — 100% killed.**

**Disposition:** accepted. Survivors are in pre-existing traversal direction ternaries the slice did not logically change; the direction arms are exercised by integration tests (GetCatalogGraphTests outgoing/incoming/all) but the unit-level GraphTraversalTests don't independently kill the Conditional(true) variants. Out of this slice's change scope; overall score comfortably clears the 80% gate. Logged as a pre-existing test-gap, not a slice defect.
