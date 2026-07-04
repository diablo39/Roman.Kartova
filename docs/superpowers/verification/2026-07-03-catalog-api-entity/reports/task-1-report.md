# Task 1 Report — Domain: ApiId, ApiStyle, Api aggregate

**Slice:** Catalog API Entity (E-02.F-03.S-01)
**Task:** Task 1 (domain layer only)
**Branch:** `feat/catalog-api-entity`
**Commit:** `1c305d4` — `feat(catalog): add Api domain aggregate (E-02.F-03.S-01)`

## What was implemented

Faithful copy of the `Service`/`ServiceId` domain pattern for the new `Api` aggregate, per the plan's exact code:

- `src/Modules/Catalog/Kartova.Catalog.Domain/ApiId.cs` — `readonly record struct ApiId(Guid Value)` with `New()` + `ToString()`.
- `src/Modules/Catalog/Kartova.Catalog.Domain/ApiStyle.cs` — `enum ApiStyle { Rest, Grpc, GraphQL }`.
- `src/Modules/Catalog/Kartova.Catalog.Domain/Api.cs` — `sealed class Api : ITenantOwned, ITeamScopedResource` with private-setter properties (`Id`, `TenantId`, `DisplayName`, `Description`, `Style`, `Version`, `SpecUrl`, `TeamId`, `CreatedByUserId`, `CreatedAt`, `Xmin`), private EF ctor, private full ctor, two `Create` overloads (`TimeProvider` and explicit `DateTimeOffset createdAt`), and validation helpers (`ValidateDisplayName`, `ValidateDescription`, `ValidateVersion`, `ValidateSpecUrl`) plus inline `ArgumentException` checks for style/createdByUserId/teamId.
- `src/Modules/Catalog/Kartova.Catalog.Tests/ApiTests.cs` — 18 `[TestMethod]`s (some `[DataRow]`-parameterized) covering: valid creation, null spec URL, fresh id per call, empty/oversize display name (incl. exact-128 boundary), empty/oversize description, empty/oversize version, undefined style, relative and oversize spec URL, empty createdBy/team, and null `TimeProvider`.

No deviation from the plan's given code — this task was transcription of an already-fully-specified spec.

## TDD evidence

**RED** — wrote `ApiTests.cs` referencing not-yet-existing `Api`/`ApiStyle`, then ran:

```
dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -v q
```
Output (relevant):
```
ApiTests.cs(16,71): error CS0246: The type or namespace name 'ApiStyle' could not be found ...
ApiTests.cs(15,20): error CS0246: The type or namespace name 'Api' could not be found ...
ApiTests.cs(16,88): error CS0103: The name 'ApiStyle' does not exist in the current context ...
```
Confirms the test file was written before the production code existed and correctly fails to compile (expected per plan Step 2).

**GREEN** — added `ApiId.cs`, `ApiStyle.cs`, `Api.cs`, then re-ran the same command:

```
dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -v q
```
Output:
```
Passed!  - Failed:     0, Passed:   173, Skipped:     0, Total:   173, Duration: 19 s - Kartova.Catalog.Tests.dll (net10.0)
```
All 173 tests in the Catalog test project pass (18 new `ApiTests` + all pre-existing Catalog domain tests, unaffected). Output is clean — no warnings surfaced in the test run.

## Files changed

- `src/Modules/Catalog/Kartova.Catalog.Domain/ApiId.cs` (new)
- `src/Modules/Catalog/Kartova.Catalog.Domain/ApiStyle.cs` (new)
- `src/Modules/Catalog/Kartova.Catalog.Domain/Api.cs` (new)
- `src/Modules/Catalog/Kartova.Catalog.Tests/ApiTests.cs` (new)

All four confirmed LF-only (0 CRLF sequences via PowerShell regex scan) before commit — no line-ending violation of repo `.gitattributes`.

## Self-review

- **Completeness:** all properties, both `Create` overloads, and every validation rule from the spec's Interfaces bullet are present. Test coverage matches the plan's test file 1:1 (transcribed verbatim, not condensed).
- **Quality/style:** matches `Service.cs`/`ServiceId.cs` conventions exactly — plain-Guid backing field pattern, private EF ctor, private setters, static factory with two overloads (clock vs explicit timestamp), validation helpers named `Validate*`. XML doc comment on `Api` and `ApiStyle` preserved as given (explains `Xmin` naming rationale and FU deferrals per ADR-0111/spec §11).
- **YAGNI:** no extra members, no premature relational/derived fields (implementedByApplicationId, exposure links) — correctly deferred per the doc comment, consistent with "Task 1 is domain-only, aggregate node only."
- **Test hygiene:** uses shared static `Tenant`/`Creator`/`Team`/`Clock` fixtures and a local `Create()` helper with named-parameter overrides (mirrors `ServiceTests.cs`'s `Ep()` helper pattern); `[DataRow]` used for whitespace/empty-string boundary pairs; boundary tests (128/4096/64/2048 char limits) present both at-limit-passes and over-limit-throws.
- **No issues found** requiring changes. This was a straight transcription task and the plan's code compiled and passed on the first attempt with no adaptation needed.

## Concerns

None. Task 1 has no dependency on other tasks and introduces no cross-cutting risk — it only adds new types to the Catalog domain assembly.
