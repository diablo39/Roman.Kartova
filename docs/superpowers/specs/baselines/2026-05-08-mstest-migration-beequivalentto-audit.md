# MSTest migration — FluentAssertions `BeEquivalentTo` audit

**Date:** 2026-05-08
**Branch:** `feat/mstest-migration-phase-0`
**Total sites:** 12

## Scope of usage

Every call site is **collection equivalence on a flat sequence of primitives** (strings or `Guid`s). No site uses non-trivial options: there is **no** `Excluding(...)`, `Including(...)`, `WithStrictOrdering()`, `Using<T>(...)`, custom `IEquivalencyStep`, or recursive object-graph comparison. No site uses `BeEquivalentTo` for record/DTO comparison.

That makes the rewrite mechanical: every site collapses to either
`CollectionAssert.AreEquivalent(expected, actual.ToArray())` (order-independent — the FA default) or, where ordering is part of the contract being asserted (e.g. `AllowedFieldNames` is documented as ordered), `CollectionAssert.AreEqual(expected, actual.ToArray())`.

## Sites

| File | Line | What's being compared | Suggested replacement |
|---|---|---|---|
| `src/Modules/Catalog/Kartova.Catalog.Tests/ListApplicationsHandlerTests.cs` | 47 | `InvalidSortFieldException.AllowedFields` (string set) vs `["createdAt", "name"]` | `CollectionAssert.AreEquivalent(new[] { "createdAt", "name" }, ex.AllowedFields.ToArray())` |
| `src/Modules/Catalog/Kartova.Catalog.Tests/ListApplicationsHandlerTests.cs` | 54 | `ApplicationSortSpecs.AllowedFieldNames` (string sequence — order documented) vs `["createdAt", "name"]` | `CollectionAssert.AreEqual(new[] { "createdAt", "name" }, ApplicationSortSpecs.AllowedFieldNames.ToArray())` (preserve order) |
| `src/Modules/Catalog/Kartova.Catalog.Tests/ListApplicationsHandlerFilterTests.cs` | 112 | `page.Items.Select(i => i.Name)` (2 strings) vs `["active-app", "decomm-app"]` — order not asserted | `CollectionAssert.AreEquivalent(new[] { "active-app", "decomm-app" }, page.Items.Select(i => i.Name).ToArray())` |
| `tests/Kartova.SharedKernel.Tests/TenantContextAccessorTests.cs` | 114 | `sut.Roles` vs `new[] { "OrgAdmin", "Member", "Viewer" }` — "exact collection" semantics | `CollectionAssert.AreEqual(new[] { "OrgAdmin", "Member", "Viewer" }, sut.Roles.ToArray())` (test name says "exact") |
| `tests/Kartova.SharedKernel.Tests/TenantContextAccessorTests.cs` | 179 | `sut.Roles` vs `new[] { "r2", "r3" }` after re-Populate | `CollectionAssert.AreEquivalent(new[] { "r2", "r3" }, sut.Roles.ToArray())` |
| `tests/Kartova.SharedKernel.Tests/TenantContextAccessorTests.cs` | 236 | `sut.Roles` vs `new[] { "new-role-1", "new-role-2" }` after Clear+Populate | `CollectionAssert.AreEquivalent(new[] { "new-role-1", "new-role-2" }, sut.Roles.ToArray())` |
| `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs` | 50 | Keycloak `webOrigins` JSON array vs `new[] { "http://localhost:5173" }` (single element, with reason string) | `CollectionAssert.AreEquivalent(new[] { "http://localhost:5173" }, origins.ToArray(), "additional web origins would silently widen CORS for kartova-web tokens.")` |
| `tests/Kartova.Api.IntegrationTests/OpenApiTests.cs` | 87 | OpenAPI `sortBy` parameter enum vs `["createdAt", "name"]` | `CollectionAssert.AreEquivalent(new[] { "createdAt", "name" }, sortByEnum.ToArray())` |
| `tests/Kartova.Api.IntegrationTests/OpenApiTests.cs` | 90 | OpenAPI `sortOrder` parameter enum vs `["asc", "desc"]` | `CollectionAssert.AreEquivalent(new[] { "asc", "desc" }, sortOrderEnum.ToArray())` |
| `tests/Kartova.SharedKernel.AspNetCore.Tests/TenantClaimsTransformationTests.cs` | 35 | `ctx.Roles` vs `new[] { "OrgAdmin", "Member" }` after JWT claims transformation | `CollectionAssert.AreEquivalent(new[] { "OrgAdmin", "Member" }, ctx.Roles.ToArray())` |
| `tests/Kartova.SharedKernel.AspNetCore.Tests/TenantClaimsTransformationTests.cs` | 50 | `ctx.Roles` vs `new[] { "platform-admin" }` for non-tenant-scoped principal | `CollectionAssert.AreEquivalent(new[] { "platform-admin" }, ctx.Roles.ToArray())` |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs` | 445 | `IEnumerable<Guid>` (explicit query result IDs) vs `IEnumerable<Guid>` (default query result IDs) — proves two filter forms return the same set | `CollectionAssert.AreEquivalent(defaultOurs.ToArray(), explicitOurs.ToArray(), "explicit ?includeDecommissioned=false must match the default (omitted) behavior")` |

## Distribution

- `src/Modules/Catalog/**` — 4 sites (3 unit, 1 integration)
- `tests/Kartova.SharedKernel.Tests/**` — 3 sites
- `tests/Kartova.SharedKernel.AspNetCore.Tests/**` — 2 sites
- `tests/Kartova.Api.IntegrationTests/**` — 2 sites
- `tests/Kartova.ArchitectureTests/**` — 1 site

## Edge cases / things to remember during translation

1. **Ordering semantics.** FA's `BeEquivalentTo` on collections is order-independent by default. Two sites read as order-sensitive based on naming/intent and should use `CollectionAssert.AreEqual`:
   - `ListApplicationsHandlerTests.cs:54` — `AllowedFieldNames` is a documented ordered list (matches the OpenAPI `sortBy` enum order).
   - `TenantContextAccessorTests.cs:114` — test is literally named `Roles_returns_exact_collection_supplied_to_Populate`.
   The rest are genuinely order-independent.
2. **`IEnumerable<T>` materialization.** MSTest's `CollectionAssert` requires `ICollection`. Every site needs `.ToArray()` (or `.ToList()`) on the actual side; the explicit-vs-default test at `ListApplicationsPaginationTests.cs:445` needs it on both sides.
3. **Reason strings.** Two sites (`KeycloakRealmSeedRules.cs:50`, `ListApplicationsPaginationTests.cs:445`) pass a `because` string. `CollectionAssert.AreEquivalent` accepts a `message` parameter — preserve the text verbatim.
4. **No object-graph comparisons anywhere.** This audit confirms the spec's worry case (deep equivalence on records/DTOs requiring `Excluding`) does **not** materialize in the current codebase.

## Decision

**Inline replacement with `CollectionAssert.AreEquivalent` / `CollectionAssert.AreEqual`** for all 12 sites.

Rationale: every site is flat-collection-of-primitives equivalence. The MSTest stdlib already covers this case 1:1; per-property `Assert.AreEqual` is not required (the spec's worry was deep object-graph comparison, which is absent). 12 sites is well below the spec's "tolerable repetition" threshold, and no site uses FA's advanced equivalency API. **AwesomeAssertions is not needed** — the escape hatch can stay closed.

Final decision belongs to the controller / user, but the cost-benefit is clearly on the side of inline rewrites.
