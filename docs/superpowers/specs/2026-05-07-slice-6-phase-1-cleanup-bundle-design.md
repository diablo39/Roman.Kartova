# Slice 6 — Phase 1 cleanup bundle (design)

**Date:** 2026-05-07
**Author:** Roman Głogowski (AI-assisted)
**Status:** Draft, pending user review
**Scope:** Bundle of registered follow-ups from slice 3 (§13.1, §13.10) and slice 5 (§13.5, §13.6). Honest debt cleanup, no new user-visible feature surface beyond a small "Show decommissioned" checkbox.
**Stories touched:** none new — closes deferred items behind already-landed E-02.F-01 stories.
**ADRs touched:** ADR-0073 (one-paragraph addendum on `?includeDecommissioned=true` opt-in). No new ADRs.

---

## 1. Why this slice

Three slices into Phase 1 (slices 3, 4, 5 — Application register / list-detail UI / edit-lifecycle), four registered follow-ups have accumulated:

- **slice-3 §13.1** — `TimeProvider` adoption was deferred at the aggregate-factory level. `Organization.Create` and `Application.Create` still call `DateTimeOffset.UtcNow` directly. Slice 5 adopted `TimeProvider` for `Application.Deprecate`/`Decommission` — a half-migrated state.
- **slice-5 §13.5** — Same `TimeProvider` issue for `Application.Create`. Subsumed by slice-3 §13.1.
- **slice-5 §13.6** — ADR-0073 says Decommissioned entities "are filtered out of default views". Slice 5 ships gray pills only — no filter.
- **slice-3 §13.10** — `CatalogModule.RegisterForMigrator` has no test; the parallel surface in `OrganizationModule` shows surviving `NoCoverage` mutants. Mutation gate is operating on uncovered code.

Bundling these is cheaper than four separate slices: TimeProvider migration touches both modules simultaneously (one decision, one PR review pass); filter is small backend + smaller SPA delta; `RegisterForMigrator` test is ~30 minutes. Total ~2.5 days.

This slice ships **zero new user-facing features beyond the checkbox**. The value is honest debt repayment so future slices don't inherit the divergence.

## 2. Context

- Slices 0–5 merged. Last commit at design time: `53b6556` (master, clean tree).
- Slice 5 (PR #21) merged 2026-05-06: Applications edit + lifecycle (E-02.F-01.S-03 + S-04). Already adopts `TimeProvider` for `Deprecate`/`Decommission` handlers + domain methods. Aggregate factories still on `DateTime.UtcNow`.
- Slice 4 cleanup (PR #18) introduced `KartovaConnectionStrings.Require*` helpers — `RegisterForMigrator` test will reuse them.
- `Microsoft.Extensions.TimeProvider.Testing` 10.5.0 already in `Kartova.Catalog.Tests`. Not yet in `Kartova.Organization.Tests`.
- Cursor-pagination contract (ADR-0095) already governs the list-endpoint shape we're extending.

### Static-dependency sweep result

`grep -nE 'DateTime(Offset)?\.(UtcNow|Now|Today)'` against `src/`, run during brainstorming, returned 22 hits. After categorization:

| File | Type | Action |
|---|---|---|
| `Kartova.SharedKernel/DomainEvent.cs:14` | Default ctor for domain events | **Skip** — no caller raises events today |
| `Kartova.Organization.Domain/Organization.cs:29` | `Organization.Create` factory | **Migrate** |
| `Kartova.Organization.Tests/OrganizationAggregateTests.cs:16` | `BeCloseTo` flaky assertion | **Migrate** |
| `Kartova.Migrator/DevSeed.cs:59` | Migrator startup dev seed | **Skip** — startup-only, below threshold |
| `Kartova.Api/Program.cs:222` | `BUILD_TIME` env-var fallback | **Skip** — composition-root info string |
| `Kartova.Catalog.Domain/Application.cs:49` | `Application.Create` overload | **Migrate** (remove overload) |
| `Kartova.Catalog.Tests/ApplicationTests.cs:152-154` | `before/after` window assertion | **Migrate** |
| 14 × integration-test files | `.AddDays(30)` payload arithmetic against real-clock handler | **Skip** — same-rate clocks, not flaky |

Net production migration: **2 files** (`Organization.cs`, `Application.cs`). Net test migration: **2 unit-test files**. The user's "iii" (whole sweep) reduces to "i + skipped-with-rationale" once measured. The skipped items are documented inline in the spec so the rationale survives.

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | `Organization.Create` and `Application.Create` accept `TimeProvider`; no `DateTimeOffset.UtcNow`-defaulting overload survives. | Single-shape API. Half-migration is its own debt — domain unit tests get FakeTimeProvider only if all factories support it. |
| 2 | `OrganizationModule` registers `services.TryAddSingleton(TimeProvider.System)` mirroring `CatalogModule`. | Consistency. `TryAddSingleton` keeps tests' `FakeTimeProvider` overrides effective. |
| 3 | `DevSeed.cs`, `Program.cs:222`, `DomainEvent.cs:14` deliberately stay on `DateTime*.UtcNow` with comments documenting the exclusion. | Below test-flakiness threshold. Migrating adds DI overhead with no test surface to gain. |
| 4 | Integration tests keep `DateTimeOffset.UtcNow.AddDays(30)` payload arithmetic. | Handler under test uses `TimeProvider.System` (real clock); test client computes payload with same real clock. Same-rate, not flaky. Migrating to fake clock per fixture is YAGNI until real flake observed. |
| 5 | `GET /applications` default excludes `Lifecycle == Decommissioned`. Opt-in via `?includeDecommissioned=true`. | Honors ADR-0073 "filtered out of default views". `false` default is the safer choice — surprises users *less* than including by default. |
| 6 | `?includeDecommissioned` is encoded in the cursor JSON. Cursor with mismatched filter returns 400 `cursor-filter-mismatch`. | Mirrors ADR-0095 + existing `cursor-sort-mismatch` pattern. Prevents page-1-with-flag-A → page-2-with-flag-B inconsistency. |
| 7 | Legacy cursors (no `includeDecommissioned` in JSON) decode as `false`. | Backward-compatible deploy: in-flight clients holding pre-slice-6 cursors keep paging without breaking. |
| 8 | SPA toolbar gets a `<Checkbox>` "Show decommissioned" (off by default), wired through `useListUrlState`. | Minimum honor of ADR-0073 with bookmarkable URL. Doesn't pre-design the broader lifecycle filter dimension before E-05.F-01.S-02 has shaped filter UX. |
| 9 | `CatalogModuleRegisterForMigratorTests` exists with three cases: resolves DbContext, not via tenant-scope path, missing connection string throws canonical message. | Closes slice-3 §13.10. `KartovaConnectionStrings.RequireBypass` (slice-3 §13.8) is the helper under test. |
| 10 | One PR for the whole bundle, sequenced commits per task. | Smaller than slice 5, single review pass, single mutation run, single deep-review. Past slice 5 set the precedent for mixed-stack bundles. |
| 11 | ADR-0073 gets a one-paragraph addendum referencing this slice's filter implementation. No new ADR. | Filter is concrete implementation of an existing ADR rule; not a new architectural decision. |
| 12 | Mutation-sentinel rerun is **not** a slice-6 deliverable — it's part of every slice's DoD #7. Including it as a named scope item double-counts. | Slice 4 + 5 each ran their own mutation pipeline. Slice 6 will too. The slice-3 §13.6 follow-up was effectively closed by slice 4's DoD pass. |

## 4. TimeProvider migration mechanics

### 4.1 `Organization.Create`

```csharp
// Before — Organization.cs:29
public static Organization Create(Guid id, Guid tenantId, string name)
    => new Organization(id, tenantId, name, DateTimeOffset.UtcNow);

// After
public static Organization Create(Guid id, Guid tenantId, string name, TimeProvider clock)
    => new Organization(id, tenantId, name, clock.GetUtcNow());
```

Single production caller: `AdminOrganizationEndpointDelegates.CreateOrganizationAsync`. Gains `TimeProvider clock` parameter (DI-injected by ASP.NET endpoint binding).

### 4.2 `Application.Create`

```csharp
// Before — Application.cs:49 (one of two overloads)
public static Application Create(string name, ..., Guid tenantId)
    => Create(name, ..., tenantId, DateTimeOffset.UtcNow);

// After — overload removed; single canonical factory takes TimeProvider
public static Application Create(string name, ..., Guid tenantId, TimeProvider clock)
    => Create(name, ..., tenantId, clock.GetUtcNow());
```

Caller: `RegisterApplicationHandler` — gains `TimeProvider _clock` field via ctor injection (mirrors slice-5's `DeprecateApplicationHandler` exactly).

### 4.3 DI wiring

`OrganizationModule.Register(...)`:

```csharp
services.TryAddSingleton(TimeProvider.System);
```

Position: alongside other `TryAdd*` calls. Mirrors `CatalogModule.cs:100`.

### 4.4 Test updates

`Kartova.Organization.Tests` adds package:

```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.5.0" />
```

`OrganizationAggregateTests.cs:16`:

```csharp
// Before
org.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

// After
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero));
var org = Organization.Create(id, tenantId, "name", clock);
org.CreatedAt.Should().Be(clock.GetUtcNow());
```

`ApplicationTests.cs:152-154`: same pattern. Domain factory unit tests in this slice optionally extract a small `Clock(DateTimeOffset? now = null)` helper similar to `ApplicationLifecycleTests.cs:20`. Decision deferred to implementation: extract if it deduplicates ≥3 sites, otherwise inline.

### 4.5 Skipped sites with rationale

- `DevSeed.cs:59` — `// DevSeed runs once at migrator startup; injecting TimeProvider here adds DI dependency for one wall-clock read with no test surface.`
- `Program.cs:222` — `// BUILD_TIME env-var fallback is composition-root info; not a domain concern.`
- `DomainEvent.cs:14` — `// Default ctor for domain events. No caller raises events today (no event-emitting handler exists). First event-emitting code will pass timestamp explicitly via the parameterized ctor.`

## 5. Decommissioned filter

### 5.1 Backend — query/handler/endpoint

```csharp
public sealed record ListApplicationsQuery(
    int Limit,
    string? SortBy,
    string? SortOrder,
    string? Cursor,
    bool IncludeDecommissioned);
```

Handler EF predicate:

```csharp
var q = db.Applications.AsQueryable();
if (!query.IncludeDecommissioned)
    q = q.Where(a => a.Lifecycle != Lifecycle.Decommissioned);
// ... existing sort + cursor + page logic
```

Endpoint:

```csharp
.MapGet("...", (
    [FromQuery] int? limit,
    [FromQuery] string? sortBy,
    [FromQuery] string? sortOrder,
    [FromQuery] string? cursor,
    [FromQuery] bool includeDecommissioned = false,
    ...) => ...);
```

### 5.2 Cursor encoding

Existing cursor JSON shape (slice-4):

```json
{ "sortBy": "createdAt", "sortOrder": "desc", "lastValue": "...", "lastId": "..." }
```

Slice-6 shape:

```json
{ "sortBy": "...", "sortOrder": "...", "lastValue": "...", "lastId": "...", "includeDecommissioned": true }
```

Decode logic:

- Field present, type `bool` → use as cursor filter state.
- Field absent (legacy cursor) → treat as `false`. Allows in-flight clients to continue paging.
- Cursor's `includeDecommissioned` differs from query parameter → 400 `cursor-filter-mismatch` problem-type.

Problem types (extend existing `ProblemTypes.CursorSortMismatch` neighbor):

```csharp
public const string CursorFilterMismatch = "https://kartova/problems/cursor-filter-mismatch";
```

### 5.3 SPA — `ApplicationsTable`

`useListUrlState` (or its current form — confirm name during implementation) gains a boolean filter slot. Toolbar:

```tsx
<Checkbox
  isSelected={includeDecommissioned}
  onChange={setIncludeDecommissioned}
  className="...existing toolbar-control style..."
>
  Show decommissioned
</Checkbox>
```

Position: in the existing toolbar row alongside search/sort. URL state: `?includeDecommissioned=true` toggles the param. Default URL (no param) excludes Decommissioned.

Component-level test: checkbox toggle drives URL param; URL param hydrates checkbox state on direct navigation to `/applications?includeDecommissioned=true`.

### 5.4 ADR-0073 addendum

One paragraph appended to ADR-0073's "Consequences" section:

> **Implementation note (slice 6, 2026-05-07, PR #XX):** The "filtered out of default views" rule is implemented on `GET /api/v1/catalog/applications` as a default-false `?includeDecommissioned=true` query parameter. The filter state is encoded in the cursor JSON (`includeDecommissioned: bool`); cursor-filter mismatch returns 400 `cursor-filter-mismatch`. Legacy cursors without the field decode as `false` for backward-compatibility. SPA `ApplicationsTable` exposes a "Show decommissioned" checkbox in the toolbar.

No status banner change.

## 6. `CatalogModule.RegisterForMigrator` coverage parity

### 6.1 Test project

`src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/Kartova.Catalog.Infrastructure.Tests.csproj`. Verify during implementation: if the project doesn't exist, scaffold it (mirror `Kartova.Organization.Infrastructure.Tests` if present, else create with the standard test-project shape — xUnit, MSBuild SDK from solution defaults).

### 6.2 Test cases

```csharp
public sealed class CatalogModuleRegisterForMigratorTests
{
    [Fact]
    public void RegisterForMigrator_resolves_CatalogDbContext_with_bypass_connection_string()
    {
        // Arrange: IConfiguration with ConnectionStrings:Bypass set
        // Act: new CatalogModule().RegisterForMigrator(services, config); build provider
        // Assert: scope.ServiceProvider.GetRequiredService<CatalogDbContext>() resolves
        //         and DbContext's connection string matches the configured value
    }

    [Fact]
    public void RegisterForMigrator_does_not_register_via_tenant_scoped_path()
    {
        // Arrange: same as above but no ITenantScope registered
        // Act + Assert: CatalogDbContext resolves WITHOUT throwing "TenantScope is not active"
        // (proves the migrator-only path bypasses AddModuleDbContext<T> tenant decoration)
    }

    [Fact]
    public void RegisterForMigrator_throws_canonical_InvalidOperationException_when_bypass_missing()
    {
        // Arrange: IConfiguration without ConnectionStrings:Bypass
        // Act + Assert: throws InvalidOperationException with KartovaConnectionStrings.RequireBypass canonical message
    }
}
```

### 6.3 Organization parity

Verify `Kartova.Organization.Infrastructure.Tests.OrganizationModuleRegisterForMigratorTests` exists (slice-3 §13.10 wording implies it does). If missing, add the analogue here for ~10 lines of additional cost.

## 7. Implementation order (rough — finalised by writing-plans)

1. **TimeProvider — Organization side** (1b–1d): factory signature change, caller update, module registration, test package + assertions.
2. **TimeProvider — Catalog side** (1c, 1e): factory overload removal, `RegisterApplicationHandler` ctor injection, `ApplicationTests` assertion replacement.
3. **`CatalogModuleRegisterForMigratorTests`** (§6): three cases. Independent of (1), can run in parallel.
4. **Decommissioned filter — backend** (§5.1, 5.2): query record field, EF predicate, endpoint binding, cursor JSON additive change, problem-type slug, integration tests.
5. **Decommissioned filter — SPA** (§5.3): URL state, toolbar checkbox, component test.
6. **ADR-0073 addendum** (§5.4).
7. **DoD pipeline** — solution build, full tests (unit + arch + integration), `docker compose up` real-HTTP path, `/simplify`, mutation-sentinel + test-generator if survivors, `requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review`, Copilot.
8. **Push, open PR, request reviews.**

## 8. Tests inventory

| Layer | Project | New / Changed |
|---|---|---|
| Domain unit | `Kartova.Organization.Tests` | `OrganizationAggregateTests` exact-time equality (replaces `BeCloseTo`) |
| Domain unit | `Kartova.Catalog.Tests` | `ApplicationTests` exact-time equality (replaces before/after window) |
| Domain unit | `Kartova.Catalog.Tests` | `ApplicationQueryTests` — `IncludeDecommissioned` predicate (in-memory or sqlite per existing convention) |
| Architecture | `Kartova.ArchitectureTests` | (Optional) rule: aggregate factory methods must take `TimeProvider` parameter. Pin during implementation if it deduplicates regression-risk. |
| Infrastructure unit | `Kartova.Catalog.Infrastructure.Tests` (new project if needed) | `CatalogModuleRegisterForMigratorTests` — three cases per §6.2 |
| Integration | `Kartova.Catalog.IntegrationTests` | `ListApplicationsTests` — default excludes; `?includeDecommissioned=true` includes; explicit `=false` excludes |
| Integration | `Kartova.Catalog.IntegrationTests` | `ListApplicationsCursorMismatchTests` — page-2 cursor with toggled filter → 400 `cursor-filter-mismatch` |
| Integration | `Kartova.Catalog.IntegrationTests` | `ListApplicationsTests` — legacy cursor (no `includeDecommissioned` field) decodes as `false` |
| SPA component | `app/src/features/applications/__tests__/ApplicationsTable.test.tsx` | Checkbox ↔ URL param round-trip |

## 9. Definition of Done (CLAUDE.md-numbered, evidence to capture)

1. **Solution build with `TreatWarningsAsErrors=true`** — 0 warnings, 0 errors. Capture `dotnet build` output.
2. **Per-task subagent reviews** (spec-compliance + code-quality) — invoked on each task. Never skipped.
3. **`superpowers:requesting-code-review`** at slice boundary against full branch diff with this spec + plan as context.
4. **Full test suite green** — unit + architecture + integration (Testcontainers). Capture `dotnet test` summary + Vitest summary.
5. **`docker compose up` + real HTTP** — qualifies because list-endpoint contract changes (new query param + cursor field). Capture: `GET /applications?includeDecommissioned=true` happy path; cursor-filter-mismatch 400 negative path. Output captured and confirmed.
6. **`/simplify`** against branch diff — should-fix items addressed across reuse / quality / efficiency lenses or explicitly skipped with rationale.
7. **Mutation feedback loop** — `mutation-sentinel` against changed files, `test-generator` until survivors are killed or accepted. Score ≥80% per `stryker-config.json`. Document accepted survivors with `// mutation-survivor: <reason>` and a follow-up entry below.
8. **`/pr-review-toolkit:review-pr`** skill.
9. **`/deep-review`** against branch diff with spec / plan / ADRs / tests as context. Blocking + Should-fix items addressed; nits triaged.

Until all nine green, status is "implementation staged, verification pending" — not "slice 6 complete".

## 10. Success criteria

- ✅ `Organization.Create` and `Application.Create` both take `TimeProvider`. No production caller passes `DateTimeOffset.UtcNow` literally to either factory.
- ✅ `Kartova.Organization.Tests` references `Microsoft.Extensions.TimeProvider.Testing` 10.5.0; `OrganizationAggregateTests` uses `FakeTimeProvider`.
- ✅ `Kartova.Catalog.Tests.ApplicationTests` factory tests use `FakeTimeProvider`; no `BeCloseTo(DateTimeOffset.UtcNow, ...)` survives in domain unit suites.
- ✅ `GET /applications` default response excludes `Lifecycle == Decommissioned`. `?includeDecommissioned=true` includes them. Explicit `=false` matches default.
- ✅ Cursor with mismatched `includeDecommissioned` returns 400 with `type` set to `https://kartova/problems/cursor-filter-mismatch`.
- ✅ Legacy cursor (JSON without the field) decodes as `false` and pages successfully.
- ✅ SPA `ApplicationsTable` toolbar checkbox round-trips through URL.
- ✅ `CatalogModuleRegisterForMigratorTests` has three cases per §6.2, all green.
- ✅ ADR-0073 has the §5.4 addendum.
- ✅ Solution builds with `TreatWarningsAsErrors=true`. Full test suite green.
- ✅ Mutation score ≥80% per `stryker-config.json`.
- ✅ `CHECKLIST.md` updated with a note on slice-5 entries: "TimeProvider/filter follow-ups landed in slice 6 — PR #XX".

## 11. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Existing in-flight cursors break post-deploy | Decision #7: legacy cursor without `includeDecommissioned` field decodes as `false`. Pinning integration test. |
| `Application.Create` signature change ripples through many test call sites | Mechanical via `dotnet-test:migrate-static-to-wrapper` skill or manual update; signatures change in a single commit so the build is either green or red — no half-state. |
| Mutation score regresses post-migration | TimeProvider migration removes a (currently uncovered) overload; new ctor-injection path gets covered immediately by handler integration tests. DoD #7 enforces score; survivors are killed or documented. |
| SPA `useListUrlState` shape doesn't currently support boolean filters | Confirm during implementation. If shape doesn't support, extend it minimally (one boolean slot). Don't over-generalize ahead of E-05 filter slice. |
| Architecture rule "aggregate factory must take `TimeProvider`" creates false positives on future non-temporal aggregates | Decision deferred to implementation — pin the rule only if it adds value beyond the unit tests. |

## 12. Self-review

**Placeholder scan:** No "TBD" or "TODO" tokens. Two intentional defer-to-implementation calls: (a) `Clock(...)` test helper extraction, (b) optional architecture rule on factory signatures. Both are calibrated calls with explicit decision criteria.

**Type / contract consistency:**

- `IncludeDecommissioned` consistent across §5.1 (query record), §5.2 (cursor JSON), §8 (test names), §10 (success criteria).
- Cursor problem-type slug `cursor-filter-mismatch` consistent across §5.2, §8, §10.
- `TimeProvider` parameter position consistent across §4.1 (Organization), §4.2 (Application).
- `RegisterForMigrator` test casing/naming consistent across §6.1, §6.2, §8, §10.

**Scope check:** Single PR. ~12 files modified, ~4 added. Smaller than slice 5 (~37 files). Comparable in shape to slice-4-cleanup (PR #18). Not too large for one PR.

**Ambiguity check:**

- "Verify during implementation" calls in §6.1 (test project existence) and §5.3 (`useListUrlState` shape) are intentional — both are file-shape facts that don't drive design decisions. Resolved at implementation time.
- "Optional" architecture rule in §4.4 / §8 — explicit decision criteria included.

**Internal consistency:**

- Decision #4 (integration tests keep wall-clock) consistent with §2 sweep table (14 × integration files marked Skip).
- Decision #5 (default-false includeDecommissioned) consistent with §5.1, §5.3, §10.
- Decision #7 (legacy-cursor-as-false) consistent with §5.2 decode logic, §8 test inventory, §10 success criterion.
- Decision #12 (mutation rerun is DoD, not deliverable) consistent with §1 (not in "why this slice" list) and §9 (DoD #7 enforces).

**Scope compared to other slices:** smaller than slice 5 (which itself was on the larger end), comparable to slice-4-cleanup. Justified bundle.

## 13. Follow-ups (registered for future planning, not in scope)

### 13.1 API-entity URL ADR (carry-forward from slice-3 §13.5)

**Why:** Phase 1 will introduce `E-02.F-03` (sync APIs + async APIs). URL collection name unresolved.

**Trigger:** Before slice that introduces API-entity endpoints (after Service slice).

**Effort:** ~30-min ADR, zero code.

### 13.2 Successor reference on Deprecated transitions (carry-forward from slice-5 §13.4)

**Why:** ADR-0073 says Deprecated entities "MUST include a sunset_date and a successor reference (where applicable)". Slice 5 honored sunset; successor entirely deferred.

**Trigger:** With slice giving the field a consumer (notification fan-out E-06 or relationship graph E-04 — whichever ships first).

**Effort:** ~1 day backend + ~half-day SPA picker.

### 13.3 Cross-timezone sunset-date UX (carry-forward from slice-5 §13.7)

**Why:** Sunset date sent as ISO-8601 with explicit UTC; user picks date in browser TZ. Cross-TZ users may experience off-by-one-day surprises.

**Trigger:** First user-reported off-by-one-day surprise. Or when MiFID II compliance flag (E-01.F-05.S-02) introduces tenant-region awareness.

**Effort:** ~half-day.

### 13.4 `DomainEvent` default-ctor TimeProvider migration

**Why:** `DomainEvent.cs:14` keeps `DateTimeOffset.UtcNow` as default-ctor fallback. No caller raises events today, so the default never fires in production.

**Trigger:** When the first domain event is emitted from a handler (likely audit-log slice E-01.F-03.S-03).

**Effort:** ~1 hour to thread `TimeProvider` through the domain-event creation pattern + ~half-day to update any event-emitting handlers.

### 13.5 Audit-log retrofit on lifecycle transitions (carry-forward from slice-5 §13.1)

**Why:** ADR-0073 says transitions are audit-logged. Audit table is E-01.F-03.S-03, unbuilt.

**Trigger:** When E-01.F-03.S-03 ships.

**Effort:** ~half-day.

### 13.6 RBAC retrofit: backward transitions + admin override (carry-forward from slice-5 §13.2)

**Why:** ADR-0073 allows backward transitions for Org Admins + admin override on sunset-date check. RBAC is E-01.F-04.S-03, unbuilt.

**Trigger:** When E-01.F-04.S-03 ships.

**Effort:** ~half-day.

### 13.7 Notifications retrofit: lifecycle transition events (carry-forward from slice-5 §13.3)

**Why:** ADR-0073 says transitions trigger notifications to dependents. Notification infra is E-06, post-slice-5.

**Trigger:** When E-06 + E-04 are both available.

**Effort:** ~half-day.

### 13.8 Filter Decommissioned out of *all* list views (extension of this slice)

**Why:** This slice ships the filter only on Applications. Other list endpoints (none today, but Service / API / Infrastructure / Broker entities upcoming in Phase 1) need the same filter.

**Trigger:** Per-entity, as new list endpoints ship. Honor the contract.

**Effort:** Trivial copy-paste once first one is shipped.

### 13.9 Replace FluentAssertions with an open-source-licensed alternative

**Why:** FluentAssertions 7.0.0 (released 2025) introduced a paid commercial license. The Apache-2.0 license stays on 6.x. Slice 6 standardized the repo on FA 6.12.0 via Central Package Management to keep the build license-clean while a replacement is selected. Long-term FA 6.x will not receive features or security fixes.

**Scope:**
- Evaluate replacement candidates: Shouldly (BSD-3-Clause), AwesomeAssertions (a community fork of FA 7), or a dedicated migration to xUnit's built-in `Assert.*` (no third-party dependency).
- Migrate every test project's assertions to the chosen library — current count is ~500 `.Should().X` call sites across 50+ test files.
- Drop `FluentAssertions` from `Directory.Packages.props`.

**Trigger:** Standalone slice once a candidate is chosen. Not bundled with feature work — the migration is mechanical and benefits from a clean PR. Expect to schedule before the next major .NET version bump (which often surfaces transitive-dependency conflicts that highlight stagnant packages).

**Effort estimate:** ~1 day for evaluation + ADR + ~1-2 days for the mechanical migration depending on call-site count.

---

**End of design.**
