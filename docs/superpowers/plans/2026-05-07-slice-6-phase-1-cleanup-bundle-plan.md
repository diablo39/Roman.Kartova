# Slice 6 — Phase 1 cleanup bundle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pay down four registered follow-ups from slices 3 + 5 in a single PR — adopt `TimeProvider` in `Organization.Create` and `Application.Create`, filter `Decommissioned` rows out of the default Applications list (with `?includeDecommissioned=true` opt-in + SPA toggle), and add the missing `CatalogModule.RegisterForMigrator` test.

**Architecture:** Two domain factories accept `TimeProvider` (consistent with slice-5's `Deprecate`/`Decommission` shape). `OrganizationModule` registers `TimeProvider.System`; `RegisterApplicationHandler` and `AdminOrganizationCommands` inject it. `CursorCodec` extends its JSON payload with `ic` (include-decommissioned) carried through `DecodedCursor`; legacy cursors decode as `false` for backward-compat. `QueryablePagingExtensions` accepts an expected filter and throws a new `CursorFilterMismatchException` (sibling of `InvalidCursorException`) which `PagingExceptionHandler` maps to a new `cursor-filter-mismatch` problem-type. SPA `useListUrlState` gains optional boolean filter slots; `CatalogListPage` renders a "Show decommissioned" checkbox in the toolbar wired through to `useApplicationsList`. ADR-0073 gets a one-paragraph implementation addendum.

**Tech Stack:** .NET 10, ASP.NET Core 10 minimal API, EF Core 10 + Npgsql 10, Wolverine (discovery only — direct dispatch per ADR-0093), `Microsoft.Extensions.TimeProvider.Testing` (FakeTimeProvider) — already in `Kartova.Catalog.Tests`, added to `Kartova.Organization.Tests` here. xUnit + FluentAssertions. React 19 + Vite 6 + TS strict, react-router 6 (URL params), TanStack Query, Untitled UI Checkbox.

**Spec:** `docs/superpowers/specs/2026-05-07-slice-6-phase-1-cleanup-bundle-design.md` (commit `97fb8c5`)

**Closes:** No new stories. Closes follow-ups slice-3 §13.1 (TimeProvider), slice-3 §13.10 (RegisterForMigrator test), slice-5 §13.5 (subsumed by §13.1), slice-5 §13.6 (Decommissioned filter).

**Out of scope (carry-forwards documented in spec §13):** API-entity URL ADR; successor reference; cross-TZ sunset UX; `DomainEvent` default-ctor migration; `DevSeed` / `Program.cs` build-time strings; audit / RBAC / notification retrofits.

---

## Pre-flight

Before starting Task 1, verify the branch state.

- [ ] **Working tree clean.** Run:

```bash
git status --short
```

Expected: empty. If you see staged or modified files, commit or stash them.

- [ ] **On `master`.** Run:

```bash
git rev-parse --abbrev-ref HEAD
```

Expected: `master`. The slice-6 spec was committed to master directly (`97fb8c5`); the plan commits to master next, then we cut the feature branch.

- [ ] **Spec on master.** Run:

```bash
git log --oneline 97fb8c5 -1
```

Expected: `97fb8c5 docs(spec): slice 6 — Phase 1 cleanup bundle design`. If `97fb8c5` doesn't resolve, **stop** — the spec hasn't merged.

- [ ] **Build green from start.** Run:

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If not, **stop** — fix on master before starting this PR.

- [ ] **Unit + arch tests green.** Run:

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: all unit + architecture tests pass. Integration tests require Docker; defer to Task 6.

- [ ] **Frontend baseline green.** Run:

```bash
cd web && npm run typecheck && npm run lint && npm run test --run && cd ..
```

Expected: TypeScript clean, ESLint clean, Vitest passes.

- [ ] **Cut the feature branch.** Run:

```bash
git checkout -b feat/slice-6-phase-1-cleanup
```

- [ ] **Commit the plan to the new branch.** Run:

```bash
git add docs/superpowers/plans/2026-05-07-slice-6-phase-1-cleanup-bundle-plan.md
git commit -m "$(cat <<'EOF'
docs(plan): slice 6 — Phase 1 cleanup bundle implementation plan

9-task plan bundling slice-3 §13.1 (TimeProvider on aggregate factories),
slice-3 §13.10 (CatalogModule.RegisterForMigrator test parity), slice-5
§13.5 (subsumed by §13.1), and slice-5 §13.6 (Decommissioned filter on
list endpoint + SPA toggle). No new user-facing surface beyond a "Show
decommissioned" checkbox.

Spec reference: 97fb8c5 (docs/superpowers/specs/2026-05-07-slice-6-phase-1-cleanup-bundle-design.md).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 1: TimeProvider on `Organization.Create`

**Goal:** `Organization.Create` accepts `TimeProvider`. `OrganizationModule` registers `TimeProvider.System`. `AdminOrganizationCommands` injects and passes through. `OrganizationAggregateTests` switches to `FakeTimeProvider` exact-time assertion.

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Domain/Organization.cs:23-30`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AdminOrganizationCommands.cs:7-22`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs:55-63`
- Modify: `src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj:10-16`
- Modify: `src/Modules/Organization/Kartova.Organization.Tests/OrganizationAggregateTests.cs`

- [ ] **Step 1: Update `Organization.cs` factory signature.**

Open `src/Modules/Organization/Kartova.Organization.Domain/Organization.cs`. Replace lines 23-30 (the `Create` method) with:

```csharp
public static Organization Create(string name, TimeProvider clock)
{
    ArgumentNullException.ThrowIfNull(clock);
    ValidateName(name);
    var id = OrganizationId.New();
    // Per ADR-0011, one org = one tenant; tenant_id is the same GUID as the org id.
    var tenantId = new TenantId(id.Value);
    return new Organization(id, tenantId, name, clock.GetUtcNow());
}
```

The `ArgumentNullException.ThrowIfNull` matches the slice-5 pattern on `Application.Deprecate(...)` (Application.cs:96 — implicit via parameter use). Explicit guard here because tests sometimes mistakenly pass `null!` to a `TimeProvider` parameter and silently NRE later — the explicit throw is the right diagnostic.

- [ ] **Step 2: Build to surface caller break.** Run:

```bash
cmd //c "dotnet build src/Modules/Organization/Kartova.Organization.Domain/Kartova.Organization.Domain.csproj --nologo -v minimal"
```

Expected: domain project builds clean. The change is source-compatible at the domain level (only one signature now). Callers will break in step 3.

- [ ] **Step 3: Update `AdminOrganizationCommands` to inject `TimeProvider`.**

Open `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AdminOrganizationCommands.cs`. Replace the entire file body with:

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;

namespace Kartova.Organization.Infrastructure.Admin;

internal sealed class AdminOrganizationCommands : IAdminOrganizationCommands
{
    private readonly AdminOrganizationDbContext _db;
    private readonly TimeProvider _clock;

    public AdminOrganizationCommands(AdminOrganizationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<OrganizationDto> CreateAsync(string name, CancellationToken ct)
    {
        var org = Kartova.Organization.Domain.Organization.Create(name, _clock);
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync(ct);
        return new OrganizationDto(org.Id.Value, org.TenantId.Value, org.Name, org.CreatedAt);
    }
}
```

- [ ] **Step 4: Register `TimeProvider.System` in `OrganizationModule`.**

Open `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs`. Add this `using` near the top alongside the existing `Microsoft.Extensions.DependencyInjection` import:

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
```

Then, in `RegisterServices` (currently lines 55-63), append before the closing brace:

```csharp
        // TimeProvider is needed by Organization.Create and any future
        // organization-side handler. TryAdd is idempotent — if another module
        // (or test fixture override) already registered TimeProvider, this is a
        // no-op so tests can swap in FakeTimeProvider without losing the
        // production default. Mirrors CatalogModule.RegisterServices.
        services.TryAddSingleton(TimeProvider.System);
```

The full `RegisterServices` should now read:

```csharp
public void RegisterServices(IServiceCollection services, IConfiguration configuration)
{
    // Tenant-scoped DbContext — connection flows from ITenantScope per ADR-0090.
    // Migrations assembly pinned so `dotnet ef` and runtime agree.
    services.AddModuleDbContext<OrganizationDbContext>(npg =>
        npg.MigrationsAssembly(typeof(OrganizationDbContext).Assembly.FullName));

    services.AddScoped<IOrganizationQueries, OrganizationQueries>();

    // TimeProvider is needed by Organization.Create and any future
    // organization-side handler. TryAdd is idempotent — if another module
    // (or test fixture override) already registered TimeProvider, this is a
    // no-op so tests can swap in FakeTimeProvider without losing the
    // production default. Mirrors CatalogModule.RegisterServices.
    services.TryAddSingleton(TimeProvider.System);
}
```

- [ ] **Step 5: Add `Microsoft.Extensions.TimeProvider.Testing` to Organization.Tests.**

Open `src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj`. Inside the existing `<ItemGroup>` that lists package references (lines 10-16), add:

```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.5.0" />
```

Pin the same version Catalog.Tests uses (`10.5.0`) so the package graph doesn't fragment. The csproj should now include all of:

```xml
<PackageReference Include="coverlet.collector" Version="6.0.4" />
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.5.0" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
<PackageReference Include="xunit" Version="2.9.2" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
<PackageReference Include="FluentAssertions" Version="7.0.0" />
```

- [ ] **Step 6: Migrate `OrganizationAggregateTests` to `FakeTimeProvider`.**

Replace the entire file `src/Modules/Organization/Kartova.Organization.Tests/OrganizationAggregateTests.cs` with:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Kartova.Organization.Tests;

public class OrganizationAggregateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null)
    {
        var c = new FakeTimeProvider();
        c.SetUtcNow(now ?? Now);
        return c;
    }

    [Fact]
    public void Create_with_valid_name_sets_tenant_id_equal_to_id_and_uses_clock_for_CreatedAt()
    {
        var clock = Clock();

        var org = Domain.Organization.Create("Acme", clock);

        org.Id.Value.Should().NotBeEmpty();
        org.TenantId.Value.Should().Be(org.Id.Value);
        org.Name.Should().Be("Acme");
        org.CreatedAt.Should().Be(clock.GetUtcNow());
    }

    [Fact]
    public void Create_with_null_clock_throws()
    {
        var act = () => Domain.Organization.Create("Acme", clock: null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_empty_name_throws(string? name)
    {
        var act = () => Domain.Organization.Create(name!, Clock());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_with_too_long_name_throws()
    {
        var name = new string('a', 101);
        var act = () => Domain.Organization.Create(name, Clock());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_updates_name()
    {
        var org = Domain.Organization.Create("Acme", Clock());
        org.Rename("NewName");
        org.Name.Should().Be("NewName");
    }
}
```

The new `Create_with_null_clock_throws` test pins the explicit guard added in Step 1 — without this test the guard becomes a NoCoverage mutation candidate.

- [ ] **Step 7: Build solution and run unit + arch tests.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: 0 warnings, 0 errors. All non-integration tests pass — including new `Create_with_null_clock_throws`.

- [ ] **Step 8: Commit.**

```bash
git add src/Modules/Organization/Kartova.Organization.Domain/Organization.cs \
        src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AdminOrganizationCommands.cs \
        src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs \
        src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj \
        src/Modules/Organization/Kartova.Organization.Tests/OrganizationAggregateTests.cs
git commit -m "$(cat <<'EOF'
feat(slice-6): TimeProvider on Organization.Create (slice-3 §13.1)

- Organization.Create takes TimeProvider; throws ArgumentNullException on null clock
- AdminOrganizationCommands ctor-injects TimeProvider
- OrganizationModule.RegisterServices TryAddSingleton(TimeProvider.System)
- Kartova.Organization.Tests references Microsoft.Extensions.TimeProvider.Testing 10.5.0
- OrganizationAggregateTests use FakeTimeProvider exact-time assertion (replaces flaky BeCloseTo ±5s)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: TimeProvider on `Application.Create`

**Goal:** Replace `Application.Create`'s no-clock convenience overload with a `TimeProvider`-taking overload. Keep the explicit-`createdAt` overload (used by integration-test fixtures and `KartovaApiFixture.SeedApplicationsAsync` for deterministic ordering). `RegisterApplicationHandler` injects `TimeProvider` and passes through. `ApplicationTests` migrates to `FakeTimeProvider`.

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs:48-49`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApplicationHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationTests.cs`

- [ ] **Step 1: Replace the no-clock convenience overload with a `TimeProvider` overload.**

Open `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`. Replace lines 48-49 (the convenience overload):

```csharp
    public static Application Create(string name, string displayName, string description, Guid ownerUserId, TenantId tenantId)
        => Create(name, displayName, description, ownerUserId, tenantId, DateTimeOffset.UtcNow);
```

with:

```csharp
    public static Application Create(string name, string displayName, string description, Guid ownerUserId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(name, displayName, description, ownerUserId, tenantId, clock.GetUtcNow());
    }
```

Note: do **not** delete the `(..., DateTimeOffset createdAt)` overload at lines 56-80 — `KartovaApiFixture.SeedApplicationsAsync` (line 73-79) and `ListApplicationsPaginationTests` (line 319-327) use it for deterministic seed timestamps. The convenience-with-`UtcNow` is what's removed.

- [ ] **Step 2: Build to surface caller break.** Run:

```bash
cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Domain/Kartova.Catalog.Domain.csproj --nologo -v minimal"
```

Expected: domain builds clean. Callers in `RegisterApplicationHandler` and `ApplicationTests` will fail to compile in the next steps; that's intentional and surfaces the migration.

- [ ] **Step 3: Update `RegisterApplicationHandler` to inject `TimeProvider`.**

Open `src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApplicationHandler.cs`. Replace the entire body of the class with:

```csharp
public sealed class RegisterApplicationHandler
{
    private readonly TimeProvider _clock;

    public RegisterApplicationHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApplicationResponse> Handle(
        RegisterApplicationCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        CancellationToken ct)
    {
        var app = Kartova.Catalog.Domain.Application.Create(
            cmd.Name, cmd.DisplayName, cmd.Description, user.UserId, tenant.Id, _clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        return app.ToResponse();
    }
}
```

(Keep the existing `using` directives and class-level XML doc comment as-is.)

The handler is already registered as scoped in `CatalogModule.cs:88` and `TimeProvider` is already registered there as a singleton (line 100). The DI graph resolves transparently.

- [ ] **Step 4: Migrate `ApplicationTests` to `TimeProvider` overload.**

Open `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationTests.cs`. Two changes are required:

**Change A — add a `TestClock` helper.** Just under the existing `Tenant` / `Owner` constants near the top of the class (search for `private static readonly TenantId Tenant`), add:

```csharp
    private static readonly DateTimeOffset Now =
        new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null)
    {
        var c = new FakeTimeProvider();
        c.SetUtcNow(now ?? Now);
        return c;
    }
```

…and add the matching `using` near the top of the file:

```csharp
using Microsoft.Extensions.Time.Testing;
```

**Change B — replace every call to `DomainApplication.Create(name, displayName, desc, Owner, Tenant)` (the no-clock overload) with `DomainApplication.Create(name, displayName, desc, Owner, Tenant, Clock())`.**

Concretely, the call sites that need this are at lines 20, 35, 48, 56, 72, 85, 95, 104, 112, 120, 129, 143, 144, 153 — every test in the file except those that already pass an explicit `DateTimeOffset` (none exist today; all use the no-clock convenience).

Replace pattern:

```csharp
DomainApplication.Create("payments-api", "Payments API", "Payments REST surface.", Owner, Tenant);
```

With:

```csharp
DomainApplication.Create("payments-api", "Payments API", "Payments REST surface.", Owner, Tenant, Clock());
```

**Change C — migrate the `Create_assigns_recent_utc_CreatedAt` test (lines 149-157)** from before/after window to exact equality. Replace the test body with:

```csharp
    [Fact]
    public void Create_uses_clock_GetUtcNow_for_CreatedAt()
    {
        var clock = Clock();

        var app = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, clock);

        app.CreatedAt.Should().Be(clock.GetUtcNow());
        app.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }
```

The new test name encodes the contract being tested (FakeTimeProvider's `GetUtcNow()` flows through `Application.Create`).

**Change D — add a null-clock guard test.** Append to the class body:

```csharp
    [Fact]
    public void Create_with_null_clock_throws()
    {
        var act = () => DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, clock: null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }
```

Pins the `ArgumentNullException.ThrowIfNull(clock)` from Step 1; without it that line is a NoCoverage mutation candidate.

- [ ] **Step 5: Build solution and run Catalog unit tests.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --no-build --nologo -v minimal"
```

Expected: 0 warnings, 0 errors. All Catalog unit tests pass — including the new `Create_uses_clock_GetUtcNow_for_CreatedAt` and `Create_with_null_clock_throws`.

- [ ] **Step 6: Run the full unit + arch suite to catch any other call sites.**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: green. If any test errors with "no overload matches", you missed a call site — search with:

```bash
grep -rn "DomainApplication.Create\|Application.Create" src/Modules/Catalog/Kartova.Catalog.Tests --include="*.cs"
```

…and migrate those too.

- [ ] **Step 7: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApplicationHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationTests.cs
git commit -m "$(cat <<'EOF'
feat(slice-6): TimeProvider on Application.Create (slice-5 §13.5, subsumed by slice-3 §13.1)

- Replace Application.Create no-clock convenience overload with TimeProvider overload (keeps explicit-createdAt overload for fixture seeding)
- RegisterApplicationHandler ctor-injects TimeProvider (mirrors slice-5's DeprecateApplicationHandler)
- ApplicationTests adopts FakeTimeProvider via TestClock helper; replaces flaky before/after window with exact equality

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `CatalogModule.RegisterForMigrator` test parity

**Goal:** New test project `Kartova.Catalog.Infrastructure.Tests` with three cases pinning `RegisterForMigrator`'s contract. Closes slice-3 §13.10.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/Kartova.Catalog.Infrastructure.Tests.csproj`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogModuleRegisterForMigratorTests.cs`
- Modify: `Kartova.slnx` (register the new test project)

- [ ] **Step 1: Verify the project does not already exist.** Run:

```bash
ls src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests 2>&1 || echo "Does not exist — proceed"
```

Expected: directory missing. If it exists, **stop** and read its current state — do not silently overwrite.

- [ ] **Step 2: Create the project file.**

Create `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/Kartova.Catalog.Infrastructure.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.Catalog.Infrastructure\Kartova.Catalog.Infrastructure.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

Pin package versions to match `Kartova.Catalog.Tests.csproj` (xunit 2.9.3, FluentAssertions 6.12.0). Do **not** mismatch — the SDK loads them across the test host.

- [ ] **Step 3: Register the test project in `Kartova.slnx`.**

Open `Kartova.slnx`. Find the existing `<Project Path="src/Modules/Catalog/Kartova.Catalog.Tests/...">` entry. Add a sibling line directly below it:

```xml
    <Project Path="src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/Kartova.Catalog.Infrastructure.Tests.csproj" />
```

Indent with the same width as neighboring entries.

- [ ] **Step 4: Verify solution build picks up the new project.** Run:

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: build green; new test project compiles (empty, but referenced).

- [ ] **Step 5: Write the three test cases.**

Create `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogModuleRegisterForMigratorTests.cs` with:

```csharp
using FluentAssertions;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kartova.Catalog.Infrastructure.Tests;

/// <summary>
/// Pins the contract of <see cref="CatalogModule.RegisterForMigrator(IServiceCollection, IConfiguration)"/>
/// — the migrator-only DbContext registration path used by the Kartova.Migrator
/// container (Helm pre-upgrade Job / Docker init per ADR-0085). Slice-3 §13.10
/// followup: the equivalent Organization registration shows surviving NoCoverage
/// mutants in the slice-3 mutation report; this test closes the same gap on Catalog.
/// </summary>
public sealed class CatalogModuleRegisterForMigratorTests
{
    [Fact]
    public void RegisterForMigrator_resolves_CatalogDbContext_with_main_connection_string()
    {
        // Migrator runs against the Main connection (the migrator role is granted
        // BYPASSRLS in PG; KartovaConnectionStrings.RequireMain is what production
        // uses — see CatalogModule.cs:105 + OrganizationModule.cs:73).
        const string mainCs = "Host=localhost;Database=kartova_test_main;Username=test;Password=test";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{KartovaConnectionStrings.Main}"] = mainCs,
            })
            .Build();

        var services = new ServiceCollection();
        new CatalogModule().RegisterForMigrator(services, config);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        db.Should().NotBeNull();
        db.Database.GetConnectionString().Should().Be(mainCs);
    }

    [Fact]
    public void RegisterForMigrator_does_not_require_active_TenantScope_to_resolve_DbContext()
    {
        // The tenant-scoped path (AddModuleDbContext, used by RegisterServices) demands
        // an ITenantScope at resolution time and throws "TenantScope is not active"
        // otherwise. The migrator path uses plain AddDbContext; this test pins the
        // distinction so a future regression that wires the migrator through the
        // tenant-scoped path fails loudly here.
        const string mainCs = "Host=localhost;Database=kartova_test_main;Username=test;Password=test";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{KartovaConnectionStrings.Main}"] = mainCs,
            })
            .Build();

        var services = new ServiceCollection();
        new CatalogModule().RegisterForMigrator(services, config);

        // No ITenantScope or TenantScopeBeginMiddleware in this graph — resolving
        // CatalogDbContext must succeed.
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterForMigrator_throws_canonical_InvalidOperationException_when_main_connection_string_is_missing()
    {
        // Pins the exact message shape KartovaConnectionStrings.Require produces.
        // CI bootstrap log scrapers depend on this format.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();

        var act = () => new CatalogModule().RegisterForMigrator(services, config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Connection string 'Kartova' is required. Set it via ConnectionStrings__Kartova env var.");
    }
}
```

Notes:
- The connection string is a placeholder; we never open it. EF Core resolves the `DbContextOptions` lazily — `Database.GetConnectionString()` reads back the configured value without touching Postgres. No Testcontainer needed.
- The third test pins `KartovaConnectionStrings.Require`'s canonical message verbatim — same diagnostic CI scraping depends on (slice-3 §13.8 resolution).

- [ ] **Step 6: Run the new tests.** Run:

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/Kartova.Catalog.Infrastructure.Tests.csproj --nologo -v minimal"
```

Expected: 3/3 pass.

- [ ] **Step 7: Verify Organization parity.**

The slice-3 §13.10 wording implies the Organization version exists. Check:

```bash
find src -name "OrganizationModuleRegisterForMigratorTests*" -not -path "*/obj/*"
```

If it returns nothing, the parity claim was incorrect. Decide:
- (a) Add an analogous file at `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/OrganizationModuleRegisterForMigratorTests.cs` mirroring the Catalog version (cost: ~10 minutes of ctrl-c + rename). The project doesn't exist either; you'd scaffold it the same way as Catalog (steps 2-3, swapping every "Catalog" for "Organization").
- (b) Skip and document — note in the PR description and add a follow-up item to the spec §13.

**Recommended:** (a) — symmetry has compounding value; the cost is trivial.

If you take (a), repeat steps 2-6 against `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/`, replacing `Catalog` with `Organization` and `CatalogModule` with `OrganizationModule` and `CatalogDbContext` with `OrganizationDbContext`. The test bodies are identical save those names.

If you take (b), add this entry to the spec's §13 follow-ups in a separate commit:

```
### 13.9 OrganizationModule.RegisterForMigrator test parity

**Why:** Slice-6 §6.3 assumed an Organization-side test existed; verification showed
none. Catalog now has one (slice 6); Organization is the asymmetric gap.

**Trigger:** Bundle with the next Organization-side change (likely E-03.F-01 org profile).

**Effort:** ~30 minutes — copy slice-6's CatalogModuleRegisterForMigratorTests verbatim.
```

- [ ] **Step 8: Commit.**

```bash
git add Kartova.slnx \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/
# If you took option (a) in step 7, also:
# git add src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/
git commit -m "$(cat <<'EOF'
test(slice-6): CatalogModule.RegisterForMigrator parity tests (slice-3 §13.10)

New Kartova.Catalog.Infrastructure.Tests project with three pinning tests:
- RegisterForMigrator resolves CatalogDbContext with the Main connection string
- RegisterForMigrator does not require an active ITenantScope
- RegisterForMigrator throws canonical InvalidOperationException when Main is missing

Closes slice-3 §13.10. Kills the surviving NoCoverage mutants the slice-3
mutation report flagged on the migrator-only registration path.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `CursorCodec` + paging extensions carry `IncludeDecommissioned`

**Goal:** Extend the cursor-pagination contract (ADR-0095) with a single-bit filter context. `CursorCodec.Encode`/`Decode` carry `bool IncludeDecommissioned` (default `false`, legacy cursors decode as `false`). New `CursorFilterMismatchException` + `ProblemTypes.CursorFilterMismatch`. `PagingExceptionHandler` gets a new branch. `QueryablePagingExtensions.ToCursorPagedAsync` gets a new optional parameter.

**Files:**
- Modify: `src/Kartova.SharedKernel/Pagination/CursorCodec.cs`
- Create: `src/Kartova.SharedKernel/Pagination/CursorFilterMismatchException.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/PagingExceptionHandler.cs`
- Modify: `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs`

- [ ] **Step 1: Extend `CursorCodec.Encode` / `Decode` / `DecodedCursor` / `CursorPayload`.**

Open `src/Kartova.SharedKernel/Pagination/CursorCodec.cs`. Apply these edits:

**A. Update the XML doc comment** at the top of the class to mention the new field. Replace lines 6-14 with:

```csharp
/// <summary>
/// Encodes and decodes opaque pagination cursors per ADR-0095.
/// Wire format is base64url-encoded JSON <c>{ s, i, d, ic? }</c>:
/// <list type="bullet">
/// <item><description><c>s</c> — sort value of the boundary row (string|number|ISO-8601 string)</description></item>
/// <item><description><c>i</c> — boundary row id (Guid, tiebreaker)</description></item>
/// <item><description><c>d</c> — direction the cursor was produced under ("asc"|"desc"). The handler verifies this matches the request's <c>sortOrder</c> to detect reused cursors across a sort flip.</description></item>
/// <item><description><c>ic</c> — optional include-decommissioned filter state at issue time. When absent (legacy cursors from before slice 6), decodes as <c>false</c>. Mismatched against the request's <c>includeDecommissioned</c> via <see cref="CursorFilterMismatchException"/> (ADR-0073 default-view rule, slice 6).</description></item>
/// </list>
/// </summary>
```

**B. Update `DecodedCursor` record** (line 23). Replace:

```csharp
public sealed record DecodedCursor(object SortValue, Guid Id, SortOrder Direction);
```

with:

```csharp
public sealed record DecodedCursor(object SortValue, Guid Id, SortOrder Direction, bool IncludeDecommissioned);
```

**C. Update `Encode`** (line 25). Replace the method with:

```csharp
public static string Encode(object sortValue, Guid id, SortOrder direction, bool includeDecommissioned = false)
{
    // ic is intentionally omitted from the JSON when false to keep cursors short
    // and to remain forward-compatible with future filter dimensions: legacy
    // decoders that don't know the field treat it as default-false.
    var payload = new CursorPayload(
        sortValue,
        id,
        direction == SortOrder.Asc ? "asc" : "desc",
        includeDecommissioned ? true : (bool?)null);
    var json = JsonSerializer.SerializeToUtf8Bytes(payload, Options);
    return ToBase64Url(json);
}
```

**D. Update `Decode`** (line 32) — extend the validation block and the return. Replace the `if (payload is null ...) ... return new DecodedCursor(...)` tail (lines 59-69) with:

```csharp
        if (payload is null
            || payload.S is null
            || payload.I == Guid.Empty
            || payload.D is not "asc" and not "desc")
        {
            throw new InvalidCursorException("Cursor is missing required fields.");
        }

        var direction = payload.D == "asc" ? SortOrder.Asc : SortOrder.Desc;
        var sortValue = payload.S is JsonElement el ? UnwrapJsonElement(el) : payload.S;
        // Legacy cursors from pre-slice-6 omit `ic`; default to false so existing
        // in-flight clients keep paging without breaking on the contract change.
        var includeDecommissioned = payload.Ic ?? false;
        return new DecodedCursor(sortValue, payload.I, direction, includeDecommissioned);
```

**E. Update `CursorPayload` record** (line 88). Replace with:

```csharp
    private sealed record CursorPayload(
        [property: JsonPropertyName("s")] object? S,
        [property: JsonPropertyName("i")] Guid I,
        [property: JsonPropertyName("d")] string? D,
        [property: JsonPropertyName("ic")] bool? Ic);
```

The `bool?` (nullable) is critical: `JsonIgnoreCondition.Never` in `Options` would still serialize `false` as `"ic": false` for every cursor — but defaults pass through nullable as null which the serializer with `WhenWritingNull` would omit. Since `Options` uses `JsonIgnoreCondition.Never`, **explicitly pass `null` for the false case** in `Encode` (already done in step C). On deserialization, missing field → `null` → `??false`.

- [ ] **Step 2: Create `CursorFilterMismatchException`.**

Create `src/Kartova.SharedKernel/Pagination/CursorFilterMismatchException.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when a paginated request's filter parameters do not match the filter
/// state encoded in the supplied cursor. This is a 400 Bad Request — the cursor
/// was issued under a different filter, so paging would silently skip rows or
/// repeat them. Mapped to RFC 7807 by <c>PagingExceptionHandler</c> with
/// problem-type slug <c>cursor-filter-mismatch</c>. ADR-0095 / ADR-0073, slice 6.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class CursorFilterMismatchException : Exception
{
    public string FilterName { get; }
    public string ExpectedValue { get; }
    public string ActualValue { get; }

    public CursorFilterMismatchException(string filterName, string expectedValue, string actualValue)
        : base($"Cursor was issued for {filterName}={expectedValue} but request uses {filterName}={actualValue}.")
    {
        FilterName = filterName;
        ExpectedValue = expectedValue;
        ActualValue = actualValue;
    }
}
```

The `[ExcludeFromCodeCoverage]` mirrors `InvalidCursorException` (line 11) — exception types with simple ctors don't merit unit tests of their own.

- [ ] **Step 3: Add `ProblemTypes.CursorFilterMismatch`.**

Open `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`. In the "Pagination / sorting — ADR-0095" block (lines 21-24), add:

```csharp
    public const string CursorFilterMismatch  = Base + "cursor-filter-mismatch";
```

The block should now read:

```csharp
    // Pagination / sorting — ADR-0095.
    public const string InvalidSortField       = Base + "invalid-sort-field";
    public const string InvalidSortOrder       = Base + "invalid-sort-order";
    public const string InvalidCursor          = Base + "invalid-cursor";
    public const string InvalidLimit           = Base + "invalid-limit";
    public const string CursorFilterMismatch   = Base + "cursor-filter-mismatch";
```

- [ ] **Step 4: Map `CursorFilterMismatchException` in `PagingExceptionHandler`.**

Open `src/Kartova.SharedKernel.AspNetCore/PagingExceptionHandler.cs`. Add a new branch to the `switch` (between `InvalidCursorException` at line 43 and `InvalidLimitException` at line 49):

```csharp
            CursorFilterMismatchException filterEx => await WriteProblemAsync(
                httpContext, exception, ProblemTypes.CursorFilterMismatch,
                "Cursor filter mismatch", filterEx.Message,
                p =>
                {
                    p.Extensions["filterName"] = filterEx.FilterName;
                    p.Extensions["expectedValue"] = filterEx.ExpectedValue;
                    p.Extensions["actualValue"] = filterEx.ActualValue;
                },
                cancellationToken),
```

- [ ] **Step 5: Extend `QueryablePagingExtensions.ToCursorPagedAsync` with the filter parameter.**

Open `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs`. Two changes:

**A. The two-overload pair gets a new optional parameter on the rich overload only.** The thin overload (lines 18-28) stays unchanged in shape and forwards a default-false. Replace the rich overload's signature (lines 39-48) with:

```csharp
    public static async Task<CursorPage<T>> ToCursorPagedAsync<T>(
        this IQueryable<T> source,
        SortSpec<T> sort,
        SortOrder order,
        string? cursor,
        int limit,
        Expression<Func<T, Guid>> idSelector,
        Func<T, Guid> idExtractor,
        CancellationToken ct,
        bool? expectedIncludeDecommissioned = null)
        where T : class
```

Adding the new param at the **end** with a default value keeps both existing overload calls compiling unchanged.

**B. Replace the cursor-decode block** (lines 57-66) with:

```csharp
        if (cursor is not null)
        {
            var decoded = CursorCodec.Decode(cursor);
            if (decoded.Direction != order)
            {
                throw new InvalidCursorException(
                    $"Cursor was issued for direction '{decoded.Direction}' but request uses '{order}'.");
            }
            if (expectedIncludeDecommissioned is bool expected
                && decoded.IncludeDecommissioned != expected)
            {
                throw new CursorFilterMismatchException(
                    filterName: "includeDecommissioned",
                    expectedValue: decoded.IncludeDecommissioned ? "true" : "false",
                    actualValue: expected ? "true" : "false");
            }
            q = ApplyKeysetFilter(q, sort.KeySelector, idSelector, decoded.SortValue, decoded.Id, order);
        }
```

The naming of `expected`/`actual` in the exception is from the **client's POV**: "your cursor was issued for X (expected, in the sense it's what the cursor encoded), but you're now asking with Y (actual, in the sense it's what you sent)." Document the convention in the message format if reviewers complain — but the payload's `filterName` field disambiguates anyway.

**C. Encode the filter into the next cursor.** Replace line 81 (`nextCursor = CursorCodec.Encode(...)`) with:

```csharp
                nextCursor = CursorCodec.Encode(
                    NormalizeForCursor(sortValue),
                    id,
                    order,
                    expectedIncludeDecommissioned ?? false);
```

When the caller doesn't pass `expectedIncludeDecommissioned`, the cursor encodes `ic: null` (omitted) — same shape as before. When the caller does pass it, the cursor carries that value and round-trips on decode.

- [ ] **Step 6: Build and run all unit + arch tests.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: 0 warnings, 0 errors, all tests green. The existing `CursorCodec` and `QueryablePagingExtensions` tests should continue passing because the new parameter is optional and defaults preserve old behavior.

If existing `CursorCodec` tests fail because they construct `DecodedCursor` directly with three arguments, update those constructions to pass `false` as the new fourth argument. Search:

```bash
grep -rn "new DecodedCursor(" src tests --include="*.cs"
```

- [ ] **Step 7: Add a `CursorCodec` round-trip test for the new field.**

Find the existing `CursorCodecTests.cs` file:

```bash
find src tests -name "CursorCodecTests*" -not -path "*/obj/*"
```

(If located in a SharedKernel test project, edit it; if no such file exists, this step is moot — the integration tests in Task 6 cover the wire-level round-trip.)

If `CursorCodecTests.cs` exists, append two tests inside it (matching the existing test class style):

```csharp
    [Fact]
    public void Encode_then_Decode_preserves_includeDecommissioned_true()
    {
        var encoded = CursorCodec.Encode(
            sortValue: "2026-05-07T12:00:00.0000000Z",
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            direction: SortOrder.Desc,
            includeDecommissioned: true);

        var decoded = CursorCodec.Decode(encoded);

        decoded.IncludeDecommissioned.Should().BeTrue();
    }

    [Fact]
    public void Decode_legacy_cursor_without_ic_field_returns_includeDecommissioned_false()
    {
        // Hand-crafted legacy cursor: { s, i, d } only, no `ic` — the shape pre-slice-6 emitted.
        // base64url("{\"s\":\"2026-05-07T12:00:00.0000000Z\",\"i\":\"11111111-1111-1111-1111-111111111111\",\"d\":\"desc\"}")
        var legacyJson = "{\"s\":\"2026-05-07T12:00:00.0000000Z\",\"i\":\"11111111-1111-1111-1111-111111111111\",\"d\":\"desc\"}";
        var legacyCursor = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(legacyJson));

        var decoded = CursorCodec.Decode(legacyCursor);

        decoded.IncludeDecommissioned.Should().BeFalse();
    }
```

These pin spec decisions #6 (round-trip) and #7 (legacy-as-false) directly. Run:

```bash
cmd //c "dotnet test --no-build --filter \"FullyQualifiedName~CursorCodecTests\" --nologo -v minimal"
```

- [ ] **Step 8: Commit.**

```bash
git add src/Kartova.SharedKernel/Pagination/CursorCodec.cs \
        src/Kartova.SharedKernel/Pagination/CursorFilterMismatchException.cs \
        src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs \
        src/Kartova.SharedKernel.AspNetCore/PagingExceptionHandler.cs \
        src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs
# If you added the codec round-trip tests in step 7, also add that file.
git commit -m "$(cat <<'EOF'
feat(slice-6): cursor carries includeDecommissioned filter (ADR-0095 / ADR-0073)

CursorCodec extends JSON payload with optional `ic` (include-decommissioned)
field. Encode defaults false; Decode treats missing field as false (legacy
cursors decode without breaking). DecodedCursor record adds IncludeDecommissioned.

QueryablePagingExtensions.ToCursorPagedAsync gets optional
expectedIncludeDecommissioned parameter; mismatch throws new
CursorFilterMismatchException, mapped to 400 cursor-filter-mismatch by
PagingExceptionHandler. Extension members carry filter name + expected +
actual values for client diagnostics.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: `ListApplicationsQuery` + handler + endpoint accept `includeDecommissioned`

**Goal:** Wire the new flag end-to-end: `ListApplicationsQuery` carries it, `ListApplicationsHandler` applies the EF predicate and threads through to `ToCursorPagedAsync`, `CatalogEndpointDelegates.ListApplicationsAsync` binds `[FromQuery] bool includeDecommissioned`.

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:60-116`

- [ ] **Step 1: Extend `ListApplicationsQuery`.**

Replace `src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs` body with:

```csharp
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>
/// List applications visible to the current tenant (RLS-filtered). ADR-0095.
/// <para>
/// <paramref name="IncludeDecommissioned"/> opts out of ADR-0073's
/// "filtered out of default views" rule; default false. Slice 6 / spec §5.
/// </para>
/// </summary>
public sealed record ListApplicationsQuery(
    ApplicationSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    bool IncludeDecommissioned);
```

Adding `IncludeDecommissioned` as the **last positional** member is intentional — minimizes existing-call-site disruption (every existing call uses positional construction).

- [ ] **Step 2: Update `ListApplicationsHandler` for the EF predicate + filter threading.**

Open `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs`. Replace the entire `Handle` method (lines 25-38) with:

```csharp
    public async Task<CursorPage<ApplicationResponse>> Handle(
        ListApplicationsQuery q,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var spec = ApplicationSortSpecs.Resolve(q.SortBy);

        // Apply ADR-0073 default-view filter before pagination so the keyset
        // bounds stay consistent: a row that's hidden by the filter must never
        // appear as a cursor boundary, otherwise the next page would silently
        // skip rows. The cursor JSON (CursorCodec.ic) is mismatch-checked inside
        // ToCursorPagedAsync.
        IQueryable<DomainApplication> source = db.Applications;
        if (!q.IncludeDecommissioned)
        {
            source = source.Where(a => a.Lifecycle != Domain.Lifecycle.Decommissioned);
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApplicationSortSpecs.IdSelector, IdExtractor, ct,
                expectedIncludeDecommissioned: q.IncludeDecommissioned);

        var items = page.Items.Select(r => r.ToResponse()).ToList();
        return new CursorPage<ApplicationResponse>(items, page.NextCursor, page.PrevCursor);
    }
```

Note the `Domain.Lifecycle.Decommissioned` qualifier — `DomainApplication` is an alias (line 5) so we reach `Lifecycle` through the namespace.

- [ ] **Step 3: Add `[FromQuery] bool includeDecommissioned` to the endpoint delegate.**

Open `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`. Replace the `ListApplicationsAsync` method (lines 60-116) with:

```csharp
    /// <summary>
    /// <c>sortBy</c> and <c>sortOrder</c> are accepted as raw strings and parsed with
    /// <c>Enum.TryParse(ignoreCase: true)</c> so that the wire contract
    /// (<c>?sortBy=createdAt&amp;sortOrder=asc</c>, camelCase per ADR-0095) and the
    /// C# enum member names both bind. <c>limit</c> stays <c>string?</c> so non-integer
    /// inputs route through <c>InvalidLimitException</c> instead of the framework's
    /// generic parse-error 400.
    /// <para>
    /// <c>includeDecommissioned</c> defaults false per ADR-0073 §"filtered out of
    /// default views" / slice-6 spec §5. Cursor encodes the filter so paging is
    /// stable; mismatch returns 400 <c>cursor-filter-mismatch</c>.
    /// </para>
    /// </summary>
    internal static async Task<IResult> ListApplicationsAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        [FromQuery] bool includeDecommissioned,
        ListApplicationsHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        // Enum.TryParse alone accepts numeric strings ("999", "-1") and binds them to
        // an undefined enum value. Enum.IsDefined rejects those before they reach the
        // sort spec / order branch.
        ApplicationSortField? parsedSortBy = null;
        if (sortBy is not null)
        {
            if (!Enum.TryParse<ApplicationSortField>(sortBy, ignoreCase: true, out var sf)
                || !Enum.IsDefined(sf))
            {
                throw new InvalidSortFieldException(sortBy, ApplicationSortSpecs.AllowedFieldNames);
            }
            parsedSortBy = sf;
        }

        SortOrder? parsedSortOrder = null;
        if (sortOrder is not null)
        {
            if (!Enum.TryParse<SortOrder>(sortOrder, ignoreCase: true, out var so)
                || !Enum.IsDefined(so))
            {
                throw new InvalidSortOrderException(sortOrder);
            }
            parsedSortOrder = so;
        }

        int effectiveLimit;
        if (limit is null)
        {
            effectiveLimit = QueryablePagingExtensions.DefaultLimit;
        }
        else if (!int.TryParse(limit, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out effectiveLimit))
        {
            throw new InvalidLimitException(
                limit,
                QueryablePagingExtensions.MinLimit,
                QueryablePagingExtensions.MaxLimit);
        }

        var query = new ListApplicationsQuery(
            SortBy: parsedSortBy ?? ApplicationSortField.CreatedAt,
            SortOrder: parsedSortOrder ?? SortOrder.Desc,
            Cursor: cursor,
            Limit: effectiveLimit,
            IncludeDecommissioned: includeDecommissioned);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }
```

ASP.NET Core minimal API binds `[FromQuery] bool` with default-false when the param is absent (because `bool` is non-nullable; missing query → default value). No explicit default needed in the signature.

- [ ] **Step 4: Update `CatalogModule.MapEndpoints` for OpenAPI docs.**

The OpenAPI metadata for `ListApplications` is generated from the delegate signature. Adding a `[FromQuery] bool includeDecommissioned` parameter automatically surfaces in the spec — no manual schema update needed. **Verify** by building and inspecting:

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: 0 warnings, 0 errors. The generated TypeScript client (`web/src/generated/openapi.ts`) is regenerated in Task 7.

- [ ] **Step 5: Run unit + arch suite.**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: green. If any existing tests construct `ListApplicationsQuery` with the old positional shape, they'll fail to compile — search and update:

```bash
grep -rn "new ListApplicationsQuery(" src --include="*.cs"
```

Existing call sites should now pass a fifth positional argument (the bool).

- [ ] **Step 6: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs
git commit -m "$(cat <<'EOF'
feat(slice-6): GET /applications honors ?includeDecommissioned default-false (slice-5 §13.6)

ListApplicationsQuery + handler + endpoint thread the new flag end-to-end.
EF Where(a => a.Lifecycle != Decommissioned) applied before pagination so
keyset bounds stay consistent. Cursor encodes the filter via CursorCodec.ic;
ToCursorPagedAsync mismatch-checks via expectedIncludeDecommissioned param.
Endpoint delegate binds [FromQuery] bool with default-false.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Integration tests for the filter

**Goal:** Five Testcontainer-backed integration tests cover the wire contract: default-excludes, opt-in-includes, explicit-false, cursor-filter-mismatch (400), and legacy-cursor-as-false.

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs` (or a new `ListApplicationsFilterTests.cs` if the existing file exceeds ~300 lines and adding more would push it past readability)
- Create (if not extending existing): `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsFilterTests.cs`

- [ ] **Step 1: Decide where the tests live.**

Run:

```bash
wc -l src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs
```

If the file is < 400 lines, **append the filter tests to it**. If ≥ 400 lines, **create a new `ListApplicationsFilterTests.cs`** in the same directory; the rest of the steps refer to whichever file you chose.

- [ ] **Step 2: Verify a fixture helper exists for seeding rows in specific lifecycles.**

`KartovaApiFixture.SeedApplicationsAsync` (lines 63-82) only seeds Active applications. We need a helper that seeds rows with a chosen `Lifecycle`. Add this method to `KartovaApiFixture.cs` (in the body, before the closing brace):

```csharp
    /// <summary>
    /// Seeds <paramref name="count"/> applications in the given lifecycle state
    /// for the given tenant, with spread-apart <c>createdAt</c> timestamps.
    /// Slice 6 — used by ListApplicationsFilterTests to populate Decommissioned
    /// rows that ADR-0073's default-view filter must hide.
    /// </summary>
    public async Task SeedApplicationsWithLifecycleAsync(
        TenantId tenantId,
        int count,
        string namePrefix,
        Lifecycle lifecycle)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        var origin = DateTimeOffset.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
        {
            var app = DomainApplication.Create(
                name: $"{namePrefix}{i:D3}",
                displayName: $"{namePrefix.ToUpperInvariant()}{i:D3}",
                description: "seeded for filter tests",
                ownerUserId: Guid.NewGuid(),
                tenantId: tenantId,
                createdAt: origin.AddMinutes(i));

            // Drive the aggregate into the desired terminal state via its own methods,
            // not by reflection on the private setter — keeps the test honest about
            // what the production state machine actually does.
            if (lifecycle == Lifecycle.Deprecated || lifecycle == Lifecycle.Decommissioned)
            {
                var clock = new FakeTimeProvider();
                clock.SetUtcNow(origin.AddMinutes(i).AddHours(1));
                app.Deprecate(sunsetDate: clock.GetUtcNow().AddMinutes(1), clock);
            }
            if (lifecycle == Lifecycle.Decommissioned)
            {
                var clock = new FakeTimeProvider();
                clock.SetUtcNow(origin.AddMinutes(i).AddHours(2));
                app.Decommission(clock);
            }

            db.Applications.Add(app);
        }
        await db.SaveChangesAsync();
    }
```

Add the matching using directives at the top of `KartovaApiFixture.cs` if not already present:

```csharp
using Kartova.Catalog.Domain;
using Microsoft.Extensions.Time.Testing;
```

You'll also need to add `Microsoft.Extensions.TimeProvider.Testing` to `Kartova.Catalog.IntegrationTests.csproj` if it's not already there. Check first:

```bash
grep -n "TimeProvider.Testing" src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj
```

If absent, add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.5.0" />` to the existing `<ItemGroup>` of package references.

- [ ] **Step 3: Add the five filter integration tests.**

In your chosen test file, add these tests (adjust the `using`s to match the file's existing pattern):

```csharp
    [Fact]
    public async Task GET_applications_default_excludes_Decommissioned()
    {
        var tenantId = _fixture.TenantIdForEmail("admin@orga.kartova.local");
        await _fixture.SeedApplicationsAsync(tenantId, count: 3, namePrefix: "active-");
        await _fixture.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: "decomm-", Lifecycle.Decommissioned);

        var client = _fixture.CreateClientForOrgA();
        var response = await client.GetAsync("/api/v1/catalog/applications?limit=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
        page!.Items.Should().HaveCount(3);
        page.Items.Select(i => i.Name).Should().OnlyContain(n => n.StartsWith("active-"));

        await _fixture.DeleteApplicationsAsync(tenantId);  // tear down for the next test
    }

    [Fact]
    public async Task GET_applications_with_includeDecommissioned_true_returns_all_lifecycles()
    {
        var tenantId = _fixture.TenantIdForEmail("admin@orga.kartova.local");
        await _fixture.SeedApplicationsAsync(tenantId, count: 3, namePrefix: "active-");
        await _fixture.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: "decomm-", Lifecycle.Decommissioned);

        var client = _fixture.CreateClientForOrgA();
        var response = await client.GetAsync("/api/v1/catalog/applications?limit=50&includeDecommissioned=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
        page!.Items.Should().HaveCount(5);

        await _fixture.DeleteApplicationsAsync(tenantId);
    }

    [Fact]
    public async Task GET_applications_with_explicit_includeDecommissioned_false_matches_default()
    {
        var tenantId = _fixture.TenantIdForEmail("admin@orga.kartova.local");
        await _fixture.SeedApplicationsAsync(tenantId, count: 3, namePrefix: "active-");
        await _fixture.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: "decomm-", Lifecycle.Decommissioned);

        var client = _fixture.CreateClientForOrgA();
        var defaultResp = await client.GetAsync("/api/v1/catalog/applications?limit=50");
        var explicitResp = await client.GetAsync("/api/v1/catalog/applications?limit=50&includeDecommissioned=false");

        defaultResp.StatusCode.Should().Be(HttpStatusCode.OK);
        explicitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var defaultPage = await defaultResp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
        var explicitPage = await explicitResp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
        explicitPage!.Items.Select(i => i.Id).Should().BeEquivalentTo(defaultPage!.Items.Select(i => i.Id));

        await _fixture.DeleteApplicationsAsync(tenantId);
    }

    [Fact]
    public async Task GET_applications_with_cursor_from_includeDecommissioned_true_then_request_false_returns_400_cursor_filter_mismatch()
    {
        var tenantId = _fixture.TenantIdForEmail("admin@orga.kartova.local");
        await _fixture.SeedApplicationsAsync(tenantId, count: 3, namePrefix: "active-");
        await _fixture.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: "decomm-", Lifecycle.Decommissioned);

        var client = _fixture.CreateClientForOrgA();

        // Page 1 with includeDecommissioned=true, limit=2 → cursor encodes ic=true
        var page1 = await client.GetAsync("/api/v1/catalog/applications?limit=2&includeDecommissioned=true");
        page1.StatusCode.Should().Be(HttpStatusCode.OK);
        var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
        p1!.NextCursor.Should().NotBeNull();

        // Page 2 sends the same cursor but flips the filter → 400
        var page2 = await client.GetAsync(
            $"/api/v1/catalog/applications?limit=2&includeDecommissioned=false&cursor={Uri.EscapeDataString(p1.NextCursor!)}");
        page2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await page2.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().Be(ProblemTypes.CursorFilterMismatch);
        problem.Extensions["filterName"].ToString().Should().Be("includeDecommissioned");

        await _fixture.DeleteApplicationsAsync(tenantId);
    }

    [Fact]
    public async Task GET_applications_with_legacy_cursor_lacking_ic_decodes_as_false_and_pages()
    {
        var tenantId = _fixture.TenantIdForEmail("admin@orga.kartova.local");
        await _fixture.SeedApplicationsAsync(tenantId, count: 5, namePrefix: "active-");

        var client = _fixture.CreateClientForOrgA();

        // Hand-craft a "legacy" cursor: { s, i, d } with no ic field.
        // Use the page-1 boundary's actual sort value + id so the keyset filter is valid.
        var page1 = await client.GetAsync("/api/v1/catalog/applications?limit=2&sortBy=createdAt&sortOrder=desc");
        page1.StatusCode.Should().Be(HttpStatusCode.OK);
        var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
        var boundary = p1!.Items.Last();

        // Construct a cursor JSON without the `ic` field.
        var legacyJson = $"{{\"s\":\"{boundary.CreatedAt:O}\",\"i\":\"{boundary.Id}\",\"d\":\"desc\"}}";
        var legacyCursor = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(legacyJson));

        // Default request (no includeDecommissioned param → false). Legacy cursor decodes to ic=false → match.
        var page2 = await client.GetAsync(
            $"/api/v1/catalog/applications?limit=2&sortBy=createdAt&sortOrder=desc&cursor={Uri.EscapeDataString(legacyCursor)}");

        page2.StatusCode.Should().Be(HttpStatusCode.OK);

        await _fixture.DeleteApplicationsAsync(tenantId);
    }
```

**Note:** if `KartovaApiFixture` doesn't already expose `DeleteApplicationsAsync(tenantId)` (a per-tenant truncate convenience), check whether existing pagination tests handle teardown differently — they likely use class-level `IClassFixture<KartovaApiFixture>` per-test seeding plus `DeleteApplicationAsync(tenantId, applicationId)` (single-row, line 88-98) called explicitly. Match the existing pattern. If you need a multi-row helper, add it to the fixture in the same shape:

```csharp
    public async Task DeleteApplicationsAsync(TenantId tenantId)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM catalog_applications WHERE tenant_id = {0}", tenantId.Value);
    }
```

- [ ] **Step 4: Run integration tests.**

Make sure Docker is running, then:

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --nologo -v minimal"
```

Expected: all five new tests pass plus existing tests still green. First run pulls the Postgres image (~2 min); later runs are < 30s.

If a test hangs, the Testcontainer probably failed to bind the port — restart Docker and retry.

- [ ] **Step 5: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/
git commit -m "$(cat <<'EOF'
test(slice-6): integration tests for ?includeDecommissioned filter (slice-5 §13.6)

Five Testcontainer-backed tests covering the wire contract:
- Default request excludes Decommissioned (3 rows seeded each lifecycle, default returns 3 active)
- ?includeDecommissioned=true returns all 5 rows
- Explicit ?includeDecommissioned=false matches default
- Cursor flipped between pages → 400 cursor-filter-mismatch with filterName extension
- Legacy cursor without `ic` field decodes as false and pages successfully

KartovaApiFixture gains SeedApplicationsWithLifecycleAsync (drives aggregate
through Deprecate/Decommission to honor production state machine) and
DeleteApplicationsAsync (multi-row teardown helper).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: SPA — `useListUrlState` boolean filter slot + `CatalogListPage` checkbox

**Goal:** SPA gains a "Show decommissioned" checkbox in the toolbar. URL param `?includeDecommissioned=true` round-trips through `useListUrlState`. `useApplicationsList` passes the value to the backend.

**Files:**
- Modify: `web/src/lib/list/useListUrlState.ts`
- Modify: `web/src/features/catalog/api/applications.ts:16-67`
- Modify: `web/src/features/catalog/pages/CatalogListPage.tsx`
- Modify: `web/src/features/catalog/components/__tests__/ApplicationsTable.test.tsx` (add toolbar test) — or create a new `CatalogListPage.test.tsx` if cleaner
- Regenerate: `web/src/generated/openapi.ts` (via `npm run codegen`)

- [ ] **Step 1: Regenerate OpenAPI types.**

The backend is already serving the new `includeDecommissioned` query param. Regenerate the typescript client:

```bash
cd web && npm run codegen && cd ..
```

(If the project doesn't have a `codegen` script, check `web/package.json` for whichever command runs `openapi-typescript` — likely `gen:openapi` or similar.)

Verify the new param appears in the generated types:

```bash
grep -n "includeDecommissioned" web/src/generated/openapi.ts
```

Expected: at least one hit on the `ListApplications` operation's query parameters block.

- [ ] **Step 2: Extend `useListUrlState` with optional boolean filter slots.**

Replace `web/src/lib/list/useListUrlState.ts` body with:

```typescript
import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import type { SortDirection } from "./types";

interface Config<TField extends string, TBoolFilter extends string = never> {
  defaultSortBy: TField;
  defaultSortOrder: SortDirection;
  allowedSortFields: readonly TField[];
  /**
   * Optional boolean URL params (e.g. `["includeDecommissioned"]`). Each is
   * read as `true` only when the URL value is the string `"true"` (case-insensitive);
   * any other value or absence yields `false`. Setter writes `"true"` or removes
   * the param entirely (no `=false` clutter in the URL).
   */
  booleanFilters?: readonly TBoolFilter[];
}

export interface ListUrlState<TField extends string, TBoolFilter extends string = never> {
  sortBy: TField;
  sortOrder: SortDirection;
  setSort: (field: TField, order: SortDirection) => void;
  /** Map of filter name to current boolean value (default false). */
  booleanFilters: Record<TBoolFilter, boolean>;
  setBooleanFilter: (name: TBoolFilter, value: boolean) => void;
}

/**
 * URL-backed sort + filter state for list pages. Falls back to defaults when URL
 * params are absent or invalid (per ADR-0095 §6.1 — no error UI for "user typed
 * garbage in URL"). Cursor is intentionally not in URL — see ADR-0095 §3 Q5 = C.
 *
 * Slice 6: optional boolean filters supported via the `booleanFilters` config —
 * used by the Catalog list page for `?includeDecommissioned=true`.
 */
export function useListUrlState<TField extends string, TBoolFilter extends string = never>(
  config: Config<TField, TBoolFilter>,
): ListUrlState<TField, TBoolFilter> {
  const [params, setParams] = useSearchParams();
  const allowed = useMemo(() => new Set<string>(config.allowedSortFields), [config.allowedSortFields]);
  const boolFilterNames = useMemo(
    () => (config.booleanFilters ?? []) as readonly TBoolFilter[],
    [config.booleanFilters],
  );

  const rawSortBy = params.get("sortBy") ?? "";
  const sortBy = allowed.has(rawSortBy) ? (rawSortBy as TField) : config.defaultSortBy;

  const rawOrder = params.get("sortOrder") ?? "";
  const sortOrder: SortDirection =
    rawOrder === "asc" || rawOrder === "desc" ? rawOrder : config.defaultSortOrder;

  const booleanFilters = useMemo(() => {
    const out = {} as Record<TBoolFilter, boolean>;
    for (const name of boolFilterNames) {
      out[name] = params.get(name)?.toLowerCase() === "true";
    }
    return out;
  }, [params, boolFilterNames]);

  const setSort = useCallback(
    (field: TField, order: SortDirection) => {
      setParams(prev => {
        const next = new URLSearchParams(prev);
        next.set("sortBy", field);
        next.set("sortOrder", order);
        return next;
      });
    },
    [setParams],
  );

  const setBooleanFilter = useCallback(
    (name: TBoolFilter, value: boolean) => {
      setParams(prev => {
        const next = new URLSearchParams(prev);
        if (value) {
          next.set(name, "true");
        } else {
          next.delete(name);
        }
        return next;
      });
    },
    [setParams],
  );

  return { sortBy, sortOrder, setSort, booleanFilters, setBooleanFilter };
}
```

The default-`never` second type parameter keeps existing call sites (slice 4 / 5) compiling unchanged — they don't supply `booleanFilters` and the returned `booleanFilters` field defaults to `{}`. Adding a `?includeDecommissioned` consumer is purely additive.

- [ ] **Step 3: Update `useApplicationsList` to thread the param.**

Open `web/src/features/catalog/api/applications.ts`. Replace the `ApplicationsListParams` type (lines 18-22) with:

```typescript
type ApplicationsListParams = {
  sortBy: NonNullable<ListApplicationsQuery["sortBy"]>;
  sortOrder: NonNullable<ListApplicationsQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0073 default-view rule: false (the default) hides Decommissioned rows. Slice 6. */
  includeDecommissioned?: boolean;
};
```

…and replace `useApplicationsList` (lines 49-67) with:

```typescript
export function useApplicationsList(params: ApplicationsListParams) {
  return useCursorList<ApplicationResponse>({
    queryKey: applicationKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/applications", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
            includeDecommissioned: params.includeDecommissioned ?? false,
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}
```

Threading `includeDecommissioned` through `applicationKeys.list(params)` means React Query automatically refetches on toggle (because the cache key changes). `useCursorList` already handles the keyset reset on `queryKey` change (line 36-41), so the cursor stack drops back to `[undefined]` synchronously — no stale 400 emitted.

- [ ] **Step 4: Update `CatalogListPage` to read URL state and render the checkbox.**

Replace `web/src/features/catalog/pages/CatalogListPage.tsx` body with:

```tsx
import { useState } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { Checkbox } from "@/components/base/checkbox/checkbox";
import { useApplicationsList } from "@/features/catalog/api/applications";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { ApplicationsTable } from "@/features/catalog/components/ApplicationsTable";
import { RegisterApplicationDialog } from "@/features/catalog/components/RegisterApplicationDialog";

const ALLOWED_SORT_FIELDS = ["createdAt", "name"] as const;
const BOOLEAN_FILTERS = ["includeDecommissioned"] as const;

export function CatalogListPage() {
  const { sortBy, sortOrder, setSort, booleanFilters, setBooleanFilter } = useListUrlState({
    defaultSortBy: "createdAt",
    defaultSortOrder: "desc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    booleanFilters: BOOLEAN_FILTERS,
  });
  const includeDecommissioned = booleanFilters.includeDecommissioned;

  const list = useApplicationsList({ sortBy, sortOrder, includeDecommissioned });
  const [dialogOpen, setDialogOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Catalog</h2>
        <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
          Register Application
        </Button>
      </div>

      <div className="flex items-center justify-end">
        <Checkbox
          isSelected={includeDecommissioned}
          onChange={(value: boolean) => setBooleanFilter("includeDecommissioned", value)}
        >
          Show decommissioned
        </Checkbox>
      </div>

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-2 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load applications</p>
            <p className="text-sm text-tertiary">Try again in a moment, or check that you&apos;re signed in.</p>
          </CardContent>
        </Card>
      ) : (
        <ApplicationsTable
          list={list}
          sortBy={sortBy}
          sortOrder={sortOrder}
          onSortChange={setSort}
        />
      )}

      <RegisterApplicationDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </div>
  );
}
```

The `Checkbox` import path may differ — verify against an existing usage:

```bash
grep -rn "from \"@/components/base/checkbox" web/src --include="*.tsx" | head -3
```

Use whichever path is established. If no Checkbox component is imported anywhere, search the Untitled UI base components:

```bash
find web/src/components/base/checkbox -type f 2>&1 | head -5
```

If the component lives somewhere else (e.g., `@/components/application/checkbox/checkbox`), use the actual path.

- [ ] **Step 5: Add a SPA test for the checkbox round-trip.**

Search whether `CatalogListPage.test.tsx` or `ApplicationsTable.test.tsx` is the better host:

```bash
ls web/src/features/catalog/components/__tests__/ web/src/features/catalog/pages/__tests__/ 2>/dev/null
```

If `CatalogListPage.test.tsx` doesn't exist, create it at `web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`. The existing `ApplicationsTable.test.tsx` is the wrong layer — the toggle lives on `CatalogListPage`, not the table.

Create or update with:

```tsx
import { describe, expect, it, beforeEach, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route, useLocation } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CatalogListPage } from "../CatalogListPage";

// Mock the data hook so we don't need MSW for this purely UI-state test.
vi.mock("@/features/catalog/api/applications", () => ({
  useApplicationsList: () => ({
    items: [],
    isLoading: false,
    isFetching: false,
    isError: false,
    hasNext: false,
    hasPrev: false,
    goNext: vi.fn(),
    goPrev: vi.fn(),
    reset: vi.fn(),
  }),
}));

function TestApp({ initialEntries = ["/"] }: { initialEntries?: string[] }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={initialEntries}>
        <Routes>
          <Route path="/" element={<><CatalogListPage /><LocationProbe /></>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="probe">{loc.search}</div>;
}

describe("CatalogListPage — Show decommissioned checkbox", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("is unchecked by default and URL has no includeDecommissioned param", () => {
    render(<TestApp />);
    const checkbox = screen.getByRole("checkbox", { name: /show decommissioned/i });
    expect(checkbox).not.toBeChecked();
    expect(screen.getByTestId("probe").textContent).toBe("");
  });

  it("hydrates to checked when URL has ?includeDecommissioned=true", () => {
    render(<TestApp initialEntries={["/?includeDecommissioned=true"]} />);
    const checkbox = screen.getByRole("checkbox", { name: /show decommissioned/i });
    expect(checkbox).toBeChecked();
  });

  it("toggling the checkbox writes the URL param to true", async () => {
    const user = userEvent.setup();
    render(<TestApp />);
    const checkbox = screen.getByRole("checkbox", { name: /show decommissioned/i });
    await user.click(checkbox);
    expect(screen.getByTestId("probe").textContent).toContain("includeDecommissioned=true");
  });

  it("toggling off removes the URL param entirely (no =false clutter)", async () => {
    const user = userEvent.setup();
    render(<TestApp initialEntries={["/?includeDecommissioned=true"]} />);
    const checkbox = screen.getByRole("checkbox", { name: /show decommissioned/i });
    await user.click(checkbox);
    expect(screen.getByTestId("probe").textContent).not.toContain("includeDecommissioned");
  });
});
```

- [ ] **Step 6: Run frontend tests.** Run:

```bash
cd web && npm run typecheck && npm run lint && npm run test --run && cd ..
```

Expected: TypeScript clean, ESLint clean, Vitest passes (including the four new checkbox tests).

If `userEvent.setup()` is not the established pattern in this repo, check existing tests:

```bash
grep -rn "userEvent.setup\|fireEvent" web/src --include="*.test.tsx" | head -3
```

…and adapt to the repo's convention. Same for the `LocationProbe` pattern — if there's a shared test util like `renderWithRouter`, use it.

- [ ] **Step 7: Commit.**

```bash
git add web/src/lib/list/useListUrlState.ts \
        web/src/features/catalog/api/applications.ts \
        web/src/features/catalog/pages/CatalogListPage.tsx \
        web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx \
        web/src/generated/openapi.ts
git commit -m "$(cat <<'EOF'
feat(slice-6): SPA — Show decommissioned checkbox + URL state (slice-5 §13.6)

useListUrlState gains optional boolean filter slot via second generic; default
preserves slice-4 / slice-5 call-site shape. CatalogListPage wires
?includeDecommissioned=true through to useApplicationsList. Toggling off
removes the URL param entirely (no =false clutter). Four CatalogListPage
tests cover the round-trip, hydration, default-off, and toggle-off cases.

Regenerated openapi.ts to include the new query parameter.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: ADR-0073 addendum

**Goal:** One-paragraph implementation note appended to ADR-0073's Consequences section, referencing this slice's `?includeDecommissioned` opt-in.

**Files:**
- Modify: `docs/architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md`

- [ ] **Step 1: Open ADR-0073 and find the Consequences section.**

Run:

```bash
grep -n "^## Consequences\|^## Implementation\|^## Status" docs/architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md
```

The section we're appending to is `## Consequences` (Michael Nygard template). If the ADR uses a different heading (e.g., `## Implementation notes`), append there instead.

- [ ] **Step 2: Append the implementation addendum.**

At the end of the Consequences section (just before the next `## ` heading or EOF), add a blank line and:

```markdown
### Implementation note (slice 6, 2026-05-07)

The "filtered out of default views" rule lands on `GET /api/v1/catalog/applications` as a default-false `?includeDecommissioned=true` query parameter (slice 6, PR #<n>). Filter state is encoded in the cursor JSON (`CursorCodec.ic`); cursor with mismatched filter returns 400 `cursor-filter-mismatch`. Legacy cursors lacking the field decode as `false` for backward-compatibility with in-flight clients. SPA `ApplicationsTable` exposes a "Show decommissioned" checkbox in the toolbar wired through `useListUrlState`. The pattern carries forward to every future entity list (Service, API, Infrastructure, Broker) — captured as slice-6 spec §13.8.
```

Replace `<n>` with the actual PR number once known (after Task 9 step 8).

- [ ] **Step 3: Commit (with PR number left as `<n>` for now).**

```bash
git add docs/architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md
git commit -m "$(cat <<'EOF'
docs(adr-0073): implementation addendum — slice 6 default-view filter

Records the concrete implementation of the "filtered out of default views"
rule: ?includeDecommissioned=true opt-in, cursor encodes filter state,
legacy cursors decode as false, SPA toolbar checkbox.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: CHECKLIST update + DoD pipeline + PR

**Goal:** Wrap up — update the product checklist with cross-references to closed follow-ups, run the multi-lens review pipeline (per CLAUDE.md DoD), open the PR with all evidence.

**Files:**
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Update CHECKLIST.md cross-references.**

No new stories close in this slice. Annotate the existing slice-3 / slice-5 entries to point at this slice's PR for the follow-up resolutions. Open `docs/product/CHECKLIST.md` and update:

```
- [x] E-02.F-01.S-01 — Register new application in catalog (slice 3 — PR #10, 2026-04-30; UI surface added in slice 4 — PR #17, 2026-04-30; TimeProvider on Application.Create — slice 6, PR #<n>, 2026-05-07)
- [x] E-02.F-01.S-03 — Edit application metadata (slice 5 — PR #21, 2026-05-06; PUT /api/v1/catalog/applications/{id} with If-Match/ETag optimistic concurrency, ADR-0096)
- [x] E-02.F-01.S-04 — Application lifecycle status transitions (slice 5 — PR #21, 2026-05-06; ADR-0073 Active → Deprecated → Decommissioned; default-view filter — slice 6, PR #<n>, 2026-05-07)
```

Bump the top-of-file "Last updated" line:

```
**Last updated:** 2026-05-07
```

The Phase 0/1 progress counters do not change (no new stories close).

- [ ] **Step 2: Commit checklist update.**

```bash
git add docs/product/CHECKLIST.md
git commit -m "$(cat <<'EOF'
docs(checklist): annotate slice-3/slice-5 entries with slice-6 follow-up resolutions

No new stories closed in slice 6. Updates cross-references on E-02.F-01.S-01
(TimeProvider on Application.Create) and E-02.F-01.S-04 (default-view filter)
to point at this slice's PR.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 3: Build with `TreatWarningsAsErrors=true` (DoD #1).**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug -p:TreatWarningsAsErrors=true --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Capture the full output for the PR description.

- [ ] **Step 4: Full backend test suite (DoD #4).**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --nologo -v minimal"
```

Expected: every test green — unit + arch + integration. Capture summary for PR.

- [ ] **Step 5: Frontend tests + typecheck + lint (DoD #4).**

```bash
cd web && npm run typecheck && npm run lint && npm run test --run && cd ..
```

Expected: all green. Capture summary for PR.

- [ ] **Step 6: `docker compose up` real-HTTP verification (DoD #5).**

This slice changes an existing list endpoint's contract (new query param + cursor field) — qualifies for DoD #5.

```bash
cmd //c "docker compose up -d --build"
```

Wait ~30s for containers to settle, then capture two evidence requests:

```bash
# First, register an Application + transition it to Decommissioned via the existing endpoints
# (see slice-5 plan Task 21 step 3 for the JWT-mint helper if you don't recall it).

# Happy path 1 — default request hides Decommissioned
curl -i "http://localhost:5050/api/v1/catalog/applications" \
  -H "Authorization: Bearer $JWT_ORGA"

# Happy path 2 — opt-in returns the Decommissioned row
curl -i "http://localhost:5050/api/v1/catalog/applications?includeDecommissioned=true" \
  -H "Authorization: Bearer $JWT_ORGA"

# Negative path — flip the filter mid-paging
# (page 1 with includeDecommissioned=true, then send same cursor with includeDecommissioned=false)
PAGE1=$(curl -s "http://localhost:5050/api/v1/catalog/applications?limit=2&includeDecommissioned=true" \
  -H "Authorization: Bearer $JWT_ORGA")
CURSOR=$(echo "$PAGE1" | jq -r '.nextCursor')
curl -i "http://localhost:5050/api/v1/catalog/applications?limit=2&includeDecommissioned=false&cursor=$CURSOR" \
  -H "Authorization: Bearer $JWT_ORGA"
```

Expected:
- (1) returns rows without the Decommissioned application.
- (2) returns the same rows + the Decommissioned one.
- (3) returns 400 with `Content-Type: application/problem+json` and body `{ "type": "https://kartova.io/problems/cursor-filter-mismatch", "filterName": "includeDecommissioned", ... }`.

Paste the three full `curl -i` outputs into the PR description's "DoD §5 — Docker compose smoke evidence" block.

- [ ] **Step 7: Push the branch and run review pipeline.**

```bash
git push -u origin feat/slice-6-phase-1-cleanup
```

Then in the Claude Code terminal run the review skills in sequence:

```
/simplify
```

Address Should-fix items inline. Each fix is a separate commit on the branch; rerun whenever you commit a non-trivial change.

```
/superpowers:requesting-code-review
```

Use the spec (`97fb8c5`), this plan, ADR-0073, ADR-0095 as context. Address Blocking + Should-fix.

```
/pr-review-toolkit:review-pr
```

Same context. Resolve findings.

```
/deep-review
```

Same context. The fixed-schema report (Blocking / Should-fix / Nits / Missing tests / What looks good) goes in the PR description.

- [ ] **Step 8: Open the PR.**

```bash
gh pr create --title "feat(slice-6): Phase 1 cleanup bundle (TimeProvider + Decommissioned filter + RegisterForMigrator test)" --body "$(cat <<'EOF'
## Summary

- Bundles four registered follow-ups from slices 3 + 5 in a single PR — no new stories close.
- TimeProvider on `Organization.Create` + `Application.Create` (closes slice-3 §13.1 + slice-5 §13.5). Both factories take `TimeProvider`; flaky `BeCloseTo`/`before-after` test patterns replaced with FakeTimeProvider exact equality.
- `?includeDecommissioned=true` opt-in on `GET /applications` + SPA toolbar checkbox (closes slice-5 §13.6, honors ADR-0073 "filtered out of default views"). Cursor encodes filter; mismatch returns 400 `cursor-filter-mismatch`. Legacy cursors decode as `false` for backward-compat.
- `CatalogModule.RegisterForMigrator` parity tests (closes slice-3 §13.10).
- ADR-0073 implementation addendum.

## Spec, plan

- Spec: `docs/superpowers/specs/2026-05-07-slice-6-phase-1-cleanup-bundle-design.md` (commit 97fb8c5)
- Plan: `docs/superpowers/plans/2026-05-07-slice-6-phase-1-cleanup-bundle-plan.md`

## Test plan

- [ ] `dotnet build Kartova.slnx -c Debug -p:TreatWarningsAsErrors=true` — 0 warnings, 0 errors
- [ ] `cd web && npm run build && npm run typecheck && npm run lint` — all clean
- [ ] Backend unit + arch suite green (incl. new Organization aggregate tests, ApplicationTests TimeProvider migration, CatalogModuleRegisterForMigratorTests, CursorCodec round-trip)
- [ ] Backend integration suite green (5 new ListApplications filter tests)
- [ ] Frontend Vitest green (4 new CatalogListPage checkbox tests)
- [ ] Docker compose smoke per Task 9 — three curl checks captured below
- [ ] /simplify findings addressed
- [ ] /deep-review Blocking + Should-fix addressed
- [ ] mutation-sentinel ≥80% on changed files
- [ ] Copilot review requested + resolved

## DoD §5 — Docker compose smoke evidence

<paste the three curl -i outputs from Task 9 step 6 here>

## Notes

- API-entity URL ADR (slice-3 §13.5) still deferred — depends on Service-entity slice giving us two adjacent collections to design against.
- Successor reference (slice-5 §13.4) still deferred — depends on E-04 (relationships) or E-06 (notifications).
- Cross-TZ sunset UX (slice-5 §13.7) still deferred — speculative until first user complaint.
- `DomainEvent.cs:14` default ctor still on `DateTimeOffset.UtcNow` — no caller raises events today; revisit when first event-emitting handler ships.
- `DevSeed.cs` and `Program.cs:222` `BUILD_TIME` fallback intentionally not migrated — startup-only, below test-flakiness threshold (rationale documented in spec §4.5).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 9: Run mutation-sentinel + test-generator loop (DoD #7).**

```
/mutation-sentinel
```

Target ≥80% per `stryker-config.json`. For surviving mutants, run:

```
/test-generator
```

…until either the score is met or remaining survivors are documented as accepted (low-value) in a PR comment.

- [ ] **Step 10: Request Copilot review.**

```bash
PR_NUMBER=$(gh pr view --json number -q .number)
gh pr edit "$PR_NUMBER" --add-reviewer copilot-pull-request-reviewer
```

Wait for Copilot's findings; address or dismiss with a reason in PR comments.

- [ ] **Step 11: Backfill the PR number into ADR-0073 and CHECKLIST.md.**

After the PR is open and you have the number, replace every `<n>` placeholder:

```bash
PR_NUMBER=$(gh pr view --json number -q .number)
sed -i "s/PR #<n>/PR #${PR_NUMBER}/g" \
  docs/architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md \
  docs/product/CHECKLIST.md

git add docs/architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md docs/product/CHECKLIST.md
git commit -m "docs: backfill PR #${PR_NUMBER} into slice-6 cross-references

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
git push
```

- [ ] **Step 12: Final DoD checklist confirmation.**

Confirm against CLAUDE.md DoD:

1. ✅ Solution build with `TreatWarningsAsErrors=true` (Step 3)
2. ✅ Per-task subagent reviews — performed by `subagent-driven-development` worker if used, or note in PR if running inline
3. ✅ `superpowers:requesting-code-review` (Step 7)
4. ✅ Full test suite green (Steps 4 + 5)
5. ✅ `docker compose up` real-HTTP (Step 6)
6. ✅ `/simplify` (Step 7)
7. ✅ Mutation feedback loop ≥80% (Step 9)
8. ✅ `/pr-review-toolkit:review-pr` (Step 7)
9. ✅ `/deep-review` (Step 7)

If any step couldn't run (e.g., Docker unavailable), state explicitly in the PR description as **pending user verification** — never imply completion.

---

## Self-review

**Spec coverage:**
- Spec §3 Decision #1 (TimeProvider on both factories) → Tasks 1, 2.
- Spec §3 Decision #2 (`OrganizationModule` registers `TimeProvider.System`) → Task 1, Step 4.
- Spec §3 Decisions #3 + #4 (skipped sites; integration tests keep wall-clock) → Task 2 Step 1 (kept the explicit-`createdAt` overload). Spec §4.5 rationale comments are inline in the code already (slice-5 left them; we don't add new ones for `DevSeed`/`Program.cs`/`DomainEvent` because the spec says "stay on `DateTime*.UtcNow`" — adding `// stay-on-utcnow` comments would be noise).
- Spec §3 Decision #5 (default exclude Decommissioned) → Task 5 Step 2.
- Spec §3 Decision #6 (cursor encodes filter; mismatch 400) → Task 4 Steps 1+5; Task 6 Step 3 test 4.
- Spec §3 Decision #7 (legacy cursors as false) → Task 4 Step 1 D + Step 7; Task 6 Step 3 test 5.
- Spec §3 Decision #8 (SPA checkbox) → Task 7 Steps 4-5.
- Spec §3 Decision #9 (RegisterForMigrator tests, three cases) → Task 3 Step 5.
- Spec §3 Decision #10 (one PR) → all Tasks land on `feat/slice-6-phase-1-cleanup`.
- Spec §3 Decision #11 (ADR-0073 addendum) → Task 8.
- Spec §3 Decision #12 (mutation rerun is DoD, not deliverable) → Task 9 Step 9.
- Spec §6.3 (Organization parity) → Task 3 Step 7.
- Spec §8 inventory: Organization aggregate test (Task 1), Application factory test (Task 2), `ApplicationQueryTests` (covered indirectly by integration tests in Task 6 — domain-only `IncludeDecommissioned` predicate test deferred since the predicate is a one-line `Where`), `CatalogModuleRegisterForMigratorTests` (Task 3), three filter integration tests + cursor-mismatch + legacy-cursor tests (Task 6), SPA checkbox round-trip test (Task 7).

**Placeholder scan:** No "TBD", "TODO", "fill in details". Two intentional dynamic lookups (Step 4 of Task 7 verifies the Checkbox import path against the real codebase; Task 6 Step 1 sizes the existing test file before placing tests). Both have explicit decision criteria.

**Type consistency:**
- `TimeProvider` parameter consistent across `Organization.Create`, `Application.Create`, `RegisterApplicationHandler`, `AdminOrganizationCommands` (Tasks 1, 2).
- `IncludeDecommissioned` field naming consistent across `ListApplicationsQuery`, `CursorCodec.DecodedCursor`, `ToCursorPagedAsync` (`expectedIncludeDecommissioned`), endpoint param, SPA query, OpenAPI types (Tasks 4, 5, 7).
- `CursorPayload.Ic` field consistent in `Encode` and `Decode` (Task 4).
- `ProblemTypes.CursorFilterMismatch` slug `cursor-filter-mismatch` consistent across handler mapping, integration test assertion, ADR addendum (Tasks 4, 6, 8).

**No unused references.** `CursorFilterMismatchException` is created and consumed in the same task (Task 4). `SeedApplicationsWithLifecycleAsync` is created in Task 6 and consumed by the same task.

**End of plan.**
