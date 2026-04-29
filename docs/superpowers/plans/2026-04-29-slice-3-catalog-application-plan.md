# Slice 3 — Catalog: Register Application Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the first vertical slice in the Catalog module — register an `Application` aggregate end-to-end through the new tenant-scope hybrid filter (slice-2-followup, ADR-0090). Walking-skeleton scope: POST + GET-by-id + GET-list, no edit/lifecycle/UI.

**Architecture:** Add `Slug` and `MapEndpoints` abstract members to `IModule`; introduce `MapTenantScopedModule(slug)` + `MapAdminModule(slug)` helpers in `SharedKernel.AspNetCore`; introduce `ICurrentUser` accessor over `IHttpContextAccessor`. Catalog module ships a single `Application` aggregate with required-field domain invariants, an EF migration with RLS, three Wolverine handlers, and three minimal-API endpoints. KeyCloak realm seed gains a second tenant user for cross-tenant tests.

**Tech Stack:** .NET 10, ASP.NET Core 10 minimal API, EF Core 10 + Npgsql 10, Wolverine (mediator only — outbox deferred), NetArchTest 1.3, Testcontainers 4, xUnit, FluentAssertions, KeyCloak 26.1.

**Spec:** `docs/superpowers/specs/2026-04-29-slice-3-catalog-application-design.md`
**ADR (precursor, must merge first):** `ADR-0092` — REST API URL convention, PR https://github.com/diablo39/Roman.Kartova/pull/9

---

## Pre-flight

Before starting Task 1, verify the branch state and that ADR-0092 has merged.

- [ ] **Branch check.** Confirm `git branch --show-current` outputs `feat/slice-3-catalog-application`. If not, `git checkout feat/slice-3-catalog-application`.
- [ ] **ADR-0092 merged.** Run `gh pr view 9 --json state -q .state`. Expected: `MERGED`. If still `OPEN`, **stop** — wait for merge, then `git fetch origin && git merge origin/master --no-edit` to bring the ADR into this branch.
- [ ] **Working tree clean.** Run `git status --short`. Should show only the untracked `.claude/skills/_lib/`, `.claude/skills/pr-*/`, `.pr-skills.toml` items (unrelated to this slice). The committed slice-3 spec file should appear in `git log` not `git status`.
- [ ] **Build green from start.** Run:

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If not, **stop** — fix on master before starting this PR.

- [ ] **All existing tests green.** Run:

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: 70 tests pass (40 unit + 30 arch). Integration tests require Docker; defer to Task 14.

---

## Task 1: `MapTenantScopedModule` and `MapAdminModule` helpers

**Goal:** Provide two route-group factories that mechanically apply the URL convention from ADR-0092.

**Files:**
- Create: `src/Kartova.SharedKernel.AspNetCore/ModuleRouteExtensions.cs`
- Create: `tests/Kartova.SharedKernel.AspNetCore.Tests/ModuleRouteExtensionsTests.cs`

- [ ] **Step 1: Write the failing tests.**

```csharp
// tests/Kartova.SharedKernel.AspNetCore.Tests/ModuleRouteExtensionsTests.cs
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class ModuleRouteExtensionsTests
{
    [Fact]
    public async Task MapTenantScopedModule_groups_routes_under_api_v1_slug()
    {
        using var host = await CreateHostAsync(app =>
        {
            var group = app.MapTenantScopedModule("catalog");
            group.MapGet("/applications", () => Results.Ok("ok")).AllowAnonymous();
        });
        var client = host.GetTestClient();
        var resp = await client.GetAsync("/api/v1/catalog/applications");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapAdminModule_groups_routes_under_api_v1_admin_slug()
    {
        using var host = await CreateHostAsync(app =>
        {
            var group = app.MapAdminModule("catalog");
            group.MapGet("/applications", () => Results.Ok("ok")).AllowAnonymous();
        });
        var client = host.GetTestClient();
        var resp = await client.GetAsync("/api/v1/admin/catalog/applications");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapTenantScopedModule_skip_rule_when_slug_is_plural_collection()
    {
        // Convention: slug IS the URL segment. The "skip" rule is mechanical —
        // the module declares Slug = "organizations" so the URL reads naturally.
        using var host = await CreateHostAsync(app =>
        {
            var group = app.MapTenantScopedModule("organizations");
            group.MapGet("/me", () => Results.Ok("ok")).AllowAnonymous();
        });
        var client = host.GetTestClient();
        var resp = await client.GetAsync("/api/v1/organizations/me");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    private static async Task<IHost> CreateHostAsync(Action<WebApplication> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        // Disable RequireTenantScope auth chain for these unit tests by stubbing the policy as anonymous.
        // (Real auth is exercised at integration-test layer.)
        var app = builder.Build();
        app.UseRouting();
        configure(app);
        await app.StartAsync();
        return app;
    }
}
```

- [ ] **Step 2: Run the tests, observe RED.**

```bash
cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests --filter \"FullyQualifiedName~ModuleRouteExtensionsTests\" --nologo -v minimal"
```

Expected: compile error `CS0117: 'IEndpointRouteBuilder' does not contain a definition for 'MapTenantScopedModule'`.

- [ ] **Step 3: Implement the helpers.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/ModuleRouteExtensions.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Per ADR-0092: every module's HTTP routes are declared via these helpers,
/// which apply the URL convention and the auth/tenant-scope shape mechanically.
/// </summary>
public static class ModuleRouteExtensions
{
    /// <summary>
    /// Tenant-scoped module routes at <c>/api/v1/{slug}</c>. The slug IS the URL segment;
    /// modules with a plural primary collection (e.g. Organization → "organizations")
    /// declare it as such so the URL reads naturally without a doubled segment.
    /// </summary>
    public static RouteGroupBuilder MapTenantScopedModule(this IEndpointRouteBuilder app, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return app.MapGroup($"/api/v1/{slug}").RequireTenantScope();
    }

    /// <summary>
    /// Admin (platform-admin) module routes at <c>/api/v1/admin/{slug}</c>.
    /// The whole admin URL space is gated by the platform-admin role.
    /// </summary>
    public static RouteGroupBuilder MapAdminModule(this IEndpointRouteBuilder app, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return app.MapGroup($"/api/v1/admin/{slug}")
            .RequireAuthorization(p => p.RequireRole(KartovaRoles.PlatformAdmin));
    }
}
```

Note: `KartovaRoles.PlatformAdmin` is the existing constant (referenced from `Program.cs:110`).

The tests intentionally use `.AllowAnonymous()` on the inner endpoint to bypass auth at the unit-test layer — auth is exercised end-to-end at the integration-test layer.

- [ ] **Step 4: Run the tests, observe GREEN.**

```bash
cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests --filter \"FullyQualifiedName~ModuleRouteExtensionsTests\" --nologo -v minimal"
```

Expected: 3 pass.

- [ ] **Step 5: Commit.**

```bash
git add src/Kartova.SharedKernel.AspNetCore/ModuleRouteExtensions.cs tests/Kartova.SharedKernel.AspNetCore.Tests/ModuleRouteExtensionsTests.cs
git commit --signoff -m "feat(sharedkernel): MapTenantScopedModule + MapAdminModule helpers (ADR-0092)"
```

---

## Task 2: `ICurrentUser` accessor

**Goal:** A small abstraction over `IHttpContextAccessor` that exposes the current user's `Guid` from the JWT `sub` claim. Used by Slice 3's command handlers; will be reused by every later module.

**Files:**
- Create: `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/HttpContextCurrentUser.cs`
- Create: `tests/Kartova.SharedKernel.AspNetCore.Tests/HttpContextCurrentUserTests.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs` (or wherever DI extensions live — register the accessor)

- [ ] **Step 1: Write the failing tests.**

```csharp
// tests/Kartova.SharedKernel.AspNetCore.Tests/HttpContextCurrentUserTests.cs
using System.Security.Claims;
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class HttpContextCurrentUserTests
{
    [Fact]
    public void UserId_returns_guid_parsed_from_sub_claim()
    {
        var expected = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var sut = CreateSut(("sub", expected.ToString()));
        sut.UserId.Should().Be(expected);
    }

    [Fact]
    public void UserId_throws_when_sub_claim_missing()
    {
        var sut = CreateSut(/* no claims */);
        var act = () => _ = sut.UserId;
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sub*");
    }

    [Fact]
    public void UserId_throws_when_sub_claim_is_not_a_guid()
    {
        var sut = CreateSut(("sub", "not-a-guid"));
        var act = () => _ = sut.UserId;
        act.Should().Throw<FormatException>();
    }

    private static HttpContextCurrentUser CreateSut(params (string Type, string Value)[] claims)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "test"));
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return new HttpContextCurrentUser(accessor);
    }
}
```

- [ ] **Step 2: Run tests, observe RED (compile error — types don't exist yet).**

```bash
cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests --filter \"FullyQualifiedName~HttpContextCurrentUserTests\" --nologo -v minimal"
```

Expected: `CS0246: The type or namespace 'HttpContextCurrentUser' could not be found`.

- [ ] **Step 3: Implement the interface and accessor.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs
namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Exposes the current authenticated user's identity from the request context.
/// Caller must run inside the auth pipeline — accessing properties when no user
/// is authenticated throws.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Guid form of the JWT 'sub' claim. KeyCloak issues UUIDs for user IDs.
    /// </summary>
    Guid UserId { get; }
}
```

```csharp
// src/Kartova.SharedKernel.AspNetCore/HttpContextCurrentUser.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore;

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public HttpContextCurrentUser(IHttpContextAccessor http) => _http = http;

    public Guid UserId
    {
        get
        {
            var sub = _http.HttpContext?.User.FindFirstValue("sub")
                      ?? throw new InvalidOperationException("No 'sub' claim on current user.");
            return Guid.Parse(sub);
        }
    }
}
```

- [ ] **Step 4: Register the accessor in the DI extension.**

Locate the existing JWT auth extension (likely `JwtAuthenticationExtensions.cs` referenced from `Program.cs:47` as `AddKartovaJwtAuth`). Add to the same extension method, after the JWT registration:

```csharp
services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
```

If `AddHttpContextAccessor()` is already registered elsewhere, leave it in place — it's idempotent.

- [ ] **Step 5: Run tests, observe GREEN.**

```bash
cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests --filter \"FullyQualifiedName~HttpContextCurrentUserTests\" --nologo -v minimal"
```

Expected: 3 pass.

- [ ] **Step 6: Commit.**

```bash
git add src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs src/Kartova.SharedKernel.AspNetCore/HttpContextCurrentUser.cs src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs tests/Kartova.SharedKernel.AspNetCore.Tests/HttpContextCurrentUserTests.cs
git commit --signoff -m "feat(sharedkernel): ICurrentUser accessor over JWT sub claim"
```

---

## Task 3: `IModule.Slug` + `MapEndpoints`, adopt in both modules, refactor Program.cs

**Goal:** Promote per-module endpoint registration onto `IModule` itself. After this task, `Program.cs`'s endpoint wiring becomes a uniform `foreach` and each module owns its routes. No URL changes for existing endpoints.

**Files:**
- Modify: `src/Kartova.SharedKernel/IModule.cs` (add abstract members)
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs` (declare Slug + MapEndpoints; move existing endpoint wiring here)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (declare Slug + empty MapEndpoints)
- Modify: `src/Kartova.Api/Program.cs` (uniform foreach module.MapEndpoints)
- Move/inline: `src/Kartova.Api/Endpoints/OrganizationEndpoints.cs` and `AdminOrganizationEndpoints.cs` content into the module — these classes lived in the API project for slice-1/2; they belong on the module per ADR-0092.

The Organization endpoint code currently lives in `src/Kartova.Api/Endpoints/`. Moving it into `Kartova.Organization.Infrastructure` is the right place per ADR-0082 (module-owned). The `Endpoints/` directory in the API project becomes empty and is deleted.

- [ ] **Step 1: Add `Slug` and `MapEndpoints` to `IModule`.**

```csharp
// src/Kartova.SharedKernel/IModule.cs
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Kartova.SharedKernel;

/// <summary>
/// Every bounded-context module implements this interface. The composition
/// root enumerates all modules and invokes them in order.
/// Enforced by NetArchTest boundary rules (ADR-0082).
/// </summary>
public interface IModule
{
    string Name { get; }

    /// <summary>
    /// Lower-case kebab-case URL segment for this module — see ADR-0092.
    /// Becomes the segment after <c>/api/v1/</c> in tenant-scoped URLs and
    /// after <c>/api/v1/admin/</c> in platform-admin URLs.
    /// When the module has a plural primary collection (e.g. Organization),
    /// the slug is that plural form so the URL reads as <c>/api/v1/organizations/me</c>
    /// rather than <c>/api/v1/organization/organizations/me</c>.
    /// </summary>
    string Slug { get; }

    Type DbContextType { get; }
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    void RegisterForMigrator(IServiceCollection services, IConfiguration configuration)
        => RegisterServices(services, configuration);

    void ConfigureWolverine(WolverineOptions options);

    /// <summary>
    /// Wire this module's HTTP endpoints. Called once at startup from the composition root.
    /// Use <see cref="Kartova.SharedKernel.AspNetCore.ModuleRouteExtensions.MapTenantScopedModule"/>
    /// for tenant-scoped routes and <c>MapAdminModule</c> for admin-only routes.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder app);
}
```

The `IModule` project doesn't currently reference `Microsoft.AspNetCore.Routing`. Add the package reference to `Kartova.SharedKernel.csproj`:

```xml
<PackageReference Include="Microsoft.AspNetCore.Routing.Abstractions" Version="2.3.0" />
```

If that package isn't on NuGet under `.NET 10`, use `<FrameworkReference Include="Microsoft.AspNetCore.App" />` instead — `IEndpointRouteBuilder` is in the shared framework. **Recommended:** `FrameworkReference`, since adopting routing here imports the entire ASP.NET shared framework into SharedKernel — but that's already the case for SharedKernel.AspNetCore, and SharedKernel itself is consumed only by web-host projects (Migrator excluded — but Migrator never calls `MapEndpoints`, only `RegisterForMigrator`).

Actually, **safer move:** keep `IEndpointRouteBuilder` out of `Kartova.SharedKernel`. Move `MapEndpoints` to a separate interface that lives in `SharedKernel.AspNetCore`:

```csharp
// src/Kartova.SharedKernel.AspNetCore/IModuleEndpoints.cs
using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

public interface IModuleEndpoints
{
    void MapEndpoints(IEndpointRouteBuilder app);
}
```

Then each module class implements both `IModule` (in SharedKernel) and `IModuleEndpoints` (in SharedKernel.AspNetCore). Program.cs filters: `foreach (var m in modules.OfType<IModuleEndpoints>()) m.MapEndpoints(app);`.

**Adopt the safer move.** Update §3 above accordingly:

```csharp
// src/Kartova.SharedKernel/IModule.cs   ← only adds Slug, NOT MapEndpoints
public interface IModule
{
    string Name { get; }
    string Slug { get; }
    Type DbContextType { get; }
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    void RegisterForMigrator(IServiceCollection services, IConfiguration configuration)
        => RegisterServices(services, configuration);
    void ConfigureWolverine(WolverineOptions options);
}
```

```csharp
// src/Kartova.SharedKernel.AspNetCore/IModuleEndpoints.cs   ← new
using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Web-host-side counterpart to <see cref="Kartova.SharedKernel.IModule"/>.
/// Modules that expose HTTP endpoints implement this interface; the migrator
/// (which only runs DDL) does not depend on it.
/// </summary>
public interface IModuleEndpoints
{
    void MapEndpoints(IEndpointRouteBuilder app);
}
```

- [ ] **Step 2: Build → expect failures from missing `Slug` on both modules.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected:

```
error CS0535: 'OrganizationModule' does not implement interface member 'IModule.Slug'
error CS0535: 'CatalogModule' does not implement interface member 'IModule.Slug'
```

- [ ] **Step 3: Implement `Slug` in both modules and adopt `IModuleEndpoints`.**

`OrganizationModule` adds:

```csharp
// src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Routing;
// ... existing usings ...

[ExcludeFromCodeCoverage]
public sealed class OrganizationModule : IModule, IModuleEndpoints   // <-- IModuleEndpoints added
{
    public string Name => "organization";
    public string Slug => "organizations";              // <-- NEW; plural primary collection per skip rule
    // ... existing members ...

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Tenant-scoped routes — replaces the explicit calls in Program.cs.
        var tenant = app.MapTenantScopedModule(Slug);   // /api/v1/organizations
        tenant.MapGet("/me", OrganizationEndpointDelegates.GetMeAsync);
        tenant.MapGet("/me/admin-only", OrganizationEndpointDelegates.GetAdminOnlyAsync)
              .RequireAuthorization(p => p.RequireRole(KartovaRoles.OrgAdmin));

        // Admin (platform-admin) routes.
        var admin = app.MapAdminModule(Slug);           // /api/v1/admin/organizations
        admin.MapPost("/", AdminOrganizationEndpointDelegates.CreateAsync);
    }
}
```

The endpoint *delegates* (the static methods `GetMeAsync`, `CreateAsync`, etc.) currently live in `src/Kartova.Api/Endpoints/OrganizationEndpoints.cs` and `AdminOrganizationEndpoints.cs`. Move both files into `Kartova.Organization.Infrastructure/` (renamed to `OrganizationEndpointDelegates.cs` and `AdminOrganizationEndpointDelegates.cs`), update namespaces, and delete the originals from the API project.

The delegate files themselves contain only `internal static` methods — no infrastructure reference; they accept `IOrganizationQueries` / `IAdminOrganizationCommands` from DI as method parameters. Moving them is a namespace + project change only.

`CatalogModule` adds:

```csharp
// src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Routing;
// ... existing usings ...

[ExcludeFromCodeCoverage]
public sealed class CatalogModule : IModule, IModuleEndpoints
{
    public string Name => "catalog";
    public string Slug => "catalog";                    // <-- NEW; singular module name (no skip)
    // ... existing members ...

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Endpoints land in Tasks 9–11.
    }
}
```

- [ ] **Step 4: Refactor `Program.cs` to use the foreach.**

Replace lines 102–111 of `src/Kartova.Api/Program.cs` with:

```csharp
// Anonymous version endpoint — system-level, not module-owned.
app.MapGet("/api/v1/version", GetVersion).AllowAnonymous();

// Module endpoints — each module wires its own routes via IModuleEndpoints.
foreach (var module in modules.OfType<Kartova.SharedKernel.AspNetCore.IModuleEndpoints>())
{
    module.MapEndpoints(app);
}
```

Remove the `using Kartova.Api.Endpoints;` if it became unused, and the `using Kartova.Organization.Application;` if it became unused.

- [ ] **Step 5: Build green.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Run unit + arch tests — should still pass.**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: 70 pass (no change vs pre-flight count — the existing arch tests don't yet check `Slug`/`MapEndpoints`).

- [ ] **Step 7: Run the existing organization integration tests — should still pass (no URL change).**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --no-build --nologo -v minimal"
```

Expected: 15 pass (Docker required).

- [ ] **Step 8: Commit.**

```bash
git add src/Kartova.SharedKernel/IModule.cs src/Kartova.SharedKernel.AspNetCore/IModuleEndpoints.cs src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEndpointDelegates.cs src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AdminOrganizationEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs src/Kartova.Api/Program.cs
git rm src/Kartova.Api/Endpoints/OrganizationEndpoints.cs src/Kartova.Api/Endpoints/AdminOrganizationEndpoints.cs
git commit --signoff -m "refactor(modules): IModule.Slug + IModuleEndpoints; modules own their routes (ADR-0092)"
```

---

## Task 4: `IModuleRules` arch tests + extend `TenantScopeRules` to include `CatalogModule`

**Goal:** Pin the URL convention via reflection-based architecture tests; ensure the §6.1 tenant-scope rule covers Catalog now that Catalog will have a real DbSet.

**Files:**
- Create: `tests/Kartova.ArchitectureTests/IModuleRules.cs`
- Modify: `tests/Kartova.ArchitectureTests/TenantScopeRules.cs` (add `CatalogModule` to the §6.1 enumeration)

- [ ] **Step 1: Write the new arch tests.**

```csharp
// tests/Kartova.ArchitectureTests/IModuleRules.cs
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.ArchitectureTests;

public class IModuleRules
{
    private static IEnumerable<Type> AllModuleTypes() =>
        AssemblyRegistry.AllAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(IModule).IsAssignableFrom(t));

    [Fact]
    public void Every_IModule_implementation_declares_non_empty_Slug()
    {
        foreach (var t in AllModuleTypes())
        {
            var instance = (IModule)Activator.CreateInstance(t)!;
            instance.Slug.Should().NotBeNullOrWhiteSpace(
                because: $"module {t.Name} must declare a non-empty Slug per ADR-0092");
        }
    }

    [Fact]
    public void Every_IModule_Slug_is_lowercase_kebab_case()
    {
        var pattern = new Regex("^[a-z][a-z0-9-]*$");
        foreach (var t in AllModuleTypes())
        {
            var instance = (IModule)Activator.CreateInstance(t)!;
            pattern.IsMatch(instance.Slug).Should().BeTrue(
                because: $"module {t.Name} Slug '{instance.Slug}' must match {pattern} per ADR-0092");
        }
    }

    [Fact]
    public void Every_IModule_implementation_also_implements_IModuleEndpoints()
    {
        foreach (var t in AllModuleTypes())
        {
            typeof(IModuleEndpoints).IsAssignableFrom(t).Should().BeTrue(
                because: $"module {t.Name} must implement IModuleEndpoints to participate in MapEndpoints loop (ADR-0092)");
        }
    }
}
```

The `AssemblyRegistry.AllAssemblies` list already exists from slice-2 work — verify it includes `Kartova.Catalog.Infrastructure`. If not, add it.

- [ ] **Step 2: Extend `TenantScopeRules` to include `CatalogModule` in §6.1.**

Locate the existing `IModule[] modules = new IModule[] { ... }` declaration in `tests/Kartova.ArchitectureTests/TenantScopeRules.cs` (around line 118 per slice-2 plan). Add `new CatalogModule()`:

```csharp
var modules = new IModule[]
{
    new OrganizationModule(),
    new CatalogModule(),     // <-- NEW
};
```

If a `using Kartova.Catalog.Infrastructure;` is missing, add it.

- [ ] **Step 3: Run the new tests.**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests --no-build --filter \"FullyQualifiedName~IModuleRules|FullyQualifiedName~TenantScopeRules\" --nologo -v minimal"
```

Expected: green. The §6.1 enumeration test in `TenantScopeRules` should still pass because `CatalogModule.RegisterServices` already calls `AddModuleDbContext<CatalogDbContext>` (verified earlier).

- [ ] **Step 4: Run all arch tests.**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests --no-build --nologo -v minimal"
```

Expected: 33 pass (30 existing + 3 new).

- [ ] **Step 5: Commit.**

```bash
git add tests/Kartova.ArchitectureTests/IModuleRules.cs tests/Kartova.ArchitectureTests/TenantScopeRules.cs tests/Kartova.ArchitectureTests/AssemblyRegistry.cs
git commit --signoff -m "test(arch): IModuleRules + extend TenantScopeRules to cover CatalogModule"
```

---

## Task 5: `ApplicationId` strongly-typed wrapper

**Goal:** Mirror `OrganizationId` pattern — readonly record struct around a `Guid`. Refines spec §5.4 from plain `Guid` to a strongly-typed id, matching the project's only existing aggregate.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/ApplicationId.cs`

- [ ] **Step 1: Implement.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Domain/ApplicationId.cs
namespace Kartova.Catalog.Domain;

public readonly record struct ApplicationId(Guid Value)
{
    public static ApplicationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
```

- [ ] **Step 2: Build green.**

```bash
cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Domain --nologo -v minimal"
```

Expected: `Build succeeded`. No tests yet — will be exercised by Task 6.

- [ ] **Step 3: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/ApplicationId.cs
git commit --signoff -m "feat(catalog): ApplicationId value type"
```

---

## Task 6: `Application` aggregate + unit tests (TDD)

**Goal:** Domain aggregate with required-field invariants enforced in the factory. RED-first per test, then implement minimal aggregate to pass all.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationTests.cs`

- [ ] **Step 1: Write all the failing tests.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationTests.cs
using FluentAssertions;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Tests;

public class ApplicationTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    [Fact]
    public void Create_with_valid_args_returns_application()
    {
        var app = Application.Create("payments-api", "Payments REST surface.", Owner, Tenant);

        app.Name.Should().Be("payments-api");
        app.Description.Should().Be("Payments REST surface.");
        app.OwnerUserId.Should().Be(Owner);
        app.TenantId.Should().Be(Tenant);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Create_throws_on_empty_or_whitespace_name(string name)
    {
        var act = () => Application.Create(name, "desc", Owner, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Fact]
    public void Create_throws_on_name_over_256_chars()
    {
        var name = new string('x', 257);
        var act = () => Application.Create(name, "desc", Owner, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*256*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_on_empty_or_whitespace_description(string description)
    {
        var act = () => Application.Create("name", description, Owner, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*description*");
    }

    [Fact]
    public void Create_throws_on_empty_owner_user_id()
    {
        var act = () => Application.Create("name", "desc", Guid.Empty, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*ownerUserId*");
    }

    [Fact]
    public void Create_assigns_fresh_id_each_call()
    {
        var a = Application.Create("name", "desc", Owner, Tenant);
        var b = Application.Create("name", "desc", Owner, Tenant);
        a.Id.Should().NotBe(b.Id);
        a.Id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_assigns_recent_utc_CreatedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var app = Application.Create("name", "desc", Owner, Tenant);
        var after = DateTimeOffset.UtcNow;
        app.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        app.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }
}
```

- [ ] **Step 2: Run tests, observe RED (compile error — type doesn't exist yet).**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter \"FullyQualifiedName~ApplicationTests\" --nologo -v minimal"
```

Expected: `CS0246: The type or namespace 'Application' could not be found`.

- [ ] **Step 3: Implement the aggregate.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

public sealed class Application : ITenantOwned
{
    public ApplicationId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Application(
        ApplicationId id,
        TenantId tenantId,
        string name,
        string description,
        Guid ownerUserId,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Description = description;
        OwnerUserId = ownerUserId;
        CreatedAt = createdAt;
    }

    // EF constructor
    private Application() { }

    public static Application Create(string name, string description, Guid ownerUserId, TenantId tenantId)
    {
        ValidateName(name);
        ValidateDescription(description);
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("ownerUserId is required.", nameof(ownerUserId));
        }

        return new Application(
            ApplicationId.New(),
            tenantId,
            name,
            description,
            ownerUserId,
            DateTimeOffset.UtcNow);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Application name must not be empty.", nameof(name));
        }
        if (name.Length > 256)
        {
            throw new ArgumentException("Application name must be <= 256 characters.", nameof(name));
        }
    }

    private static void ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Application description must not be empty.", nameof(description));
        }
    }
}
```

- [ ] **Step 4: Run tests, observe GREEN.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter \"FullyQualifiedName~ApplicationTests\" --nologo -v minimal"
```

Expected: 9 pass.

- [ ] **Step 5: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationTests.cs
git commit --signoff -m "feat(catalog): Application aggregate with required-field invariants"
```

---

## Task 7: Catalog Contracts DTOs

**Goal:** Public request/response DTOs in the Contracts assembly. Excluded from coverage per CLAUDE.md.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/RegisterApplicationRequest.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs`

- [ ] **Step 1: Implement DTOs.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Contracts/RegisterApplicationRequest.cs
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record RegisterApplicationRequest(string Name, string Description);
```

```csharp
// src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record ApplicationResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string Description,
    Guid OwnerUserId,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 2: Build green.**

```bash
cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Contracts --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s)`. The `ContractsCoverageRules` arch test from CLAUDE.md will pass because both types carry `[ExcludeFromCodeCoverage]`.

- [ ] **Step 3: Run all arch tests to verify the Contracts coverage rule.**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests --no-build --nologo -v minimal"
```

Expected: 33 pass (no regression — both new DTOs are correctly excluded).

- [ ] **Step 4: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts/RegisterApplicationRequest.cs src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs
git commit --signoff -m "feat(catalog): RegisterApplicationRequest + ApplicationResponse contracts"
```

---

## Task 8: EF entity config + DbSet + migration with RLS

**Goal:** Persist `Application` to a new `catalog_applications` table with RLS enabled. EF generates the table; the RLS policy is added by manually editing the migration's `Up()`/`Down()` methods.

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs` (add `DbSet<Application>`)
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs`
- Generated: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<timestamp>_AddApplications.cs`
- Modify (manual edit): the generated migration to add RLS policy.

- [ ] **Step 1: Add `DbSet<Application>` to `CatalogDbContext`.**

Open `CatalogDbContext.cs` and add:

```csharp
public DbSet<Application> Applications => Set<Application>();
```

Add `using Kartova.Catalog.Domain;` if missing.

- [ ] **Step 2: Add EF entity config.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> b)
    {
        b.ToTable("catalog_applications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id)
            .HasConversion(v => v.Value, v => new ApplicationId(v))
            .ValueGeneratedNever();
        b.Property(x => x.TenantId)
            .HasConversion(v => v.Value, v => new TenantId(v))
            .HasColumnName("tenant_id")
            .IsRequired();
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_applications_tenant_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").IsRequired();
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
```

Wire it from `CatalogDbContext.OnModelCreating` (mirror Organization's pattern):

```csharp
modelBuilder.ApplyConfiguration(new EfApplicationConfiguration());
```

- [ ] **Step 3: Generate the migration.**

```bash
cmd //c "dotnet ef migrations add AddApplications --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Migrator --context CatalogDbContext"
```

Expected: a new file `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<YYYYMMDDhhmmss>_AddApplications.cs`. EF will create the table with the indexed `tenant_id` column. **It will NOT add the RLS policy** — that's the next step.

- [ ] **Step 4: Edit the generated migration to add RLS.**

Open the new `<timestamp>_AddApplications.cs`. At the **end of the `Up()` method** (after `migrationBuilder.CreateTable(...)`), append:

```csharp
migrationBuilder.Sql(
    "ALTER TABLE catalog_applications ENABLE ROW LEVEL SECURITY;");
migrationBuilder.Sql(@"
    CREATE POLICY catalog_applications_tenant_isolation ON catalog_applications
        USING (tenant_id = current_setting('app.current_tenant_id', true)::uuid);
");
```

At the **start of the `Down()` method** (before `migrationBuilder.DropTable(...)`), prepend:

```csharp
migrationBuilder.Sql("DROP POLICY IF EXISTS catalog_applications_tenant_isolation ON catalog_applications;");
migrationBuilder.Sql("ALTER TABLE catalog_applications DISABLE ROW LEVEL SECURITY;");
```

The pattern matches Organization's RLS migration — verify by `grep -rn "ENABLE ROW LEVEL SECURITY" src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/` and copy the exact policy syntax used there if different.

- [ ] **Step 5: Build green.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Verify the migration applies cleanly.** Spin up the local stack and check the migrator logs:

```bash
cmd //c "docker compose up -d --build"
cmd //c "docker compose logs migrator --tail=30"
```

Expected: log line `Module 'catalog' migrated.` and no errors. Then:

```bash
cmd //c "docker compose exec postgres psql -U kartova -d kartova -c \"\\d catalog_applications\""
cmd //c "docker compose exec postgres psql -U kartova -d kartova -c \"SELECT polname, polcmd FROM pg_policy WHERE polrelid = 'catalog_applications'::regclass;\""
```

Expected: table with the columns and indexes from §2; one policy row `catalog_applications_tenant_isolation`. Tear down:

```bash
cmd //c "docker compose down -v"
```

- [ ] **Step 7: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/
git commit --signoff -m "feat(catalog): EF config + migration for catalog_applications table with RLS"
```

---

## Task 9: POST `/api/v1/catalog/applications` end-to-end

**Goal:** Wire the first Catalog endpoint. Command + handler + endpoint + single-tenant integration tests. Cross-tenant tests are deferred to Task 13 (after the second KeyCloak user exists).

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/RegisterApplicationCommand.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/RegisterApplicationHandler.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ApplicationResponseExtensions.cs` (DTO mapping)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (wire endpoint in `MapEndpoints`)
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs`

- [ ] **Step 1: Write the command + handler.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/RegisterApplicationCommand.cs
namespace Kartova.Catalog.Application;

public sealed record RegisterApplicationCommand(string Name, string Description);
```

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/RegisterApplicationHandler.cs
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Application;

public sealed class RegisterApplicationHandler
{
    public async Task<ApplicationResponse> Handle(
        RegisterApplicationCommand cmd,
        CatalogDbContext db,
        ITenantScope scope,
        ICurrentUser user,
        CancellationToken ct)
    {
        var app = Application.Create(cmd.Name, cmd.Description, user.UserId, scope.TenantId);
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        return app.ToResponse();
    }
}
```

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/ApplicationResponseExtensions.cs
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

internal static class ApplicationResponseExtensions
{
    public static ApplicationResponse ToResponse(this Application app) =>
        new(app.Id.Value, app.TenantId.Value, app.Name, app.Description, app.OwnerUserId, app.CreatedAt);
}
```

The Application project must reference `Kartova.SharedKernel.AspNetCore` for `ICurrentUser` (or move `ICurrentUser` interface to plain `Kartova.SharedKernel` if cleaner — defer that refactor; SharedKernel.AspNetCore reference is acceptable for now since Application is consumed only by web hosts).

If the project ref is missing, add it:

```bash
cmd //c "dotnet add src/Modules/Catalog/Kartova.Catalog.Application reference src/Kartova.SharedKernel.AspNetCore"
```

- [ ] **Step 2: Wire the endpoint in `CatalogModule.MapEndpoints`.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs (MapEndpoints body)
public void MapEndpoints(IEndpointRouteBuilder app)
{
    var tenant = app.MapTenantScopedModule(Slug);   // /api/v1/catalog
    tenant.MapPost("/applications", CatalogEndpointDelegates.RegisterApplicationAsync)
          .WithName("RegisterApplication");
}
```

Add a new `CatalogEndpointDelegates.cs` co-located with the module:

```csharp
// src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.AspNetCore.Http;
using Wolverine;

namespace Kartova.Catalog.Infrastructure;

internal static class CatalogEndpointDelegates
{
    internal static async Task<IResult> RegisterApplicationAsync(
        RegisterApplicationRequest request,
        IMessageBus bus,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<ApplicationResponse>(
            new RegisterApplicationCommand(request.Name, request.Description), ct);
        return Results.Created($"/api/v1/catalog/applications/{response.Id}", response);
    }
}
```

The Infrastructure project must reference `Kartova.Catalog.Application` (likely already does) and `Kartova.Catalog.Contracts`.

- [ ] **Step 3: Build green.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Write integration tests for the happy + sad single-tenant paths.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.Catalog.IntegrationTests;

[Collection(KartovaApiCollection.Name)]   // existing fixture
public class RegisterApplicationTests
{
    private readonly KartovaApiFixture _fx;

    public RegisterApplicationTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task POST_with_valid_payload_creates_row_and_returns_201()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("payments-api", "Payments REST surface."));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.ToString().Should().StartWith("/api/v1/catalog/applications/");

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();
        body!.Name.Should().Be("payments-api");
        body.Description.Should().Be("Payments REST surface.");
        body.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task POST_persists_owner_user_id_from_jwt_sub_claim()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var subFromToken = await _fx.GetSubClaimAsync("admin@orga.kartova.local");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-x", "x"));
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();

        body!.OwnerUserId.Should().Be(subFromToken);
    }

    [Fact]
    public async Task POST_persists_tenant_id_from_scope_not_payload()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var tenantFromToken = await _fx.GetTenantIdClaimAsync("admin@orga.kartova.local");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-y", "y"));
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();

        body!.TenantId.Should().Be(tenantFromToken);
    }

    [Theory]
    [InlineData("", "desc")]
    [InlineData("   ", "desc")]
    [InlineData("name", "")]
    [InlineData("name", "  ")]
    public async Task POST_with_invalid_payload_returns_400(string name, string description)
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, description));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task POST_without_token_returns_401()
    {
        using var client = _fx.CreateAnonymousClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("name", "desc"));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

The fixture helpers `CreateAuthenticatedClientAsync`, `CreateAnonymousClient`, `GetSubClaimAsync`, `GetTenantIdClaimAsync` may not all exist yet on `KartovaApiFixture`. The first two exist (used by Organization's integration tests). If `GetSubClaimAsync` / `GetTenantIdClaimAsync` don't exist, **add them as small helpers** on the fixture in the same task — they decode the test user's JWT once and cache the result.

If exact helpers are absent, write inline equivalents in the test (decode JWT via `System.IdentityModel.Tokens.Jwt`).

- [ ] **Step 5: Run integration tests (Docker required).**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --no-build --filter \"FullyQualifiedName~RegisterApplicationTests\" --nologo -v minimal"
```

Expected: 7 (or 8, depending on theory rows) pass.

- [ ] **Step 6: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs
git commit --signoff -m "feat(catalog): POST /api/v1/catalog/applications + integration tests"
```

---

## Task 10: GET `/api/v1/catalog/applications/{id}` end-to-end

**Goal:** Read by id within the current tenant; 404 for not-found.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/GetApplicationByIdQuery.cs` + handler
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add `GetByIdAsync` delegate)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (wire endpoint)
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs` (add the GET-by-id tests)

- [ ] **Step 1: Implement the query + handler.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/GetApplicationByIdQuery.cs
using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.Application;

public sealed record GetApplicationByIdQuery(Guid Id);

public sealed class GetApplicationByIdHandler
{
    public async Task<ApplicationResponse?> Handle(
        GetApplicationByIdQuery q,
        Kartova.Catalog.Infrastructure.CatalogDbContext db,
        CancellationToken ct)
    {
        var app = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.Applications, x => x.Id.Value == q.Id, ct);
        return app?.ToResponse();
    }
}
```

(EF's tenant-scope filter / RLS ensures cross-tenant rows are invisible.)

- [ ] **Step 2: Add the endpoint delegate and wire it.**

In `CatalogEndpointDelegates.cs` add:

```csharp
internal static async Task<IResult> GetApplicationByIdAsync(
    Guid id,
    IMessageBus bus,
    CancellationToken ct)
{
    var resp = await bus.InvokeAsync<ApplicationResponse?>(new GetApplicationByIdQuery(id), ct);
    if (resp is null)
    {
        return Results.Problem(
            type: "https://kartova.io/problems/resource-not-found",
            title: "Application not found",
            detail: "No application with that id is visible in the current tenant.",
            statusCode: StatusCodes.Status404NotFound);
    }
    return Results.Ok(resp);
}
```

In `CatalogModule.MapEndpoints`:

```csharp
tenant.MapGet("/applications/{id:guid}", CatalogEndpointDelegates.GetApplicationByIdAsync)
      .WithName("GetApplicationById");
```

- [ ] **Step 3: Build green.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Add integration tests.**

```csharp
[Fact]
public async Task GET_by_id_returns_row_in_same_tenant()
{
    var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
    var post = await client.PostAsJsonAsync(
        "/api/v1/catalog/applications",
        new RegisterApplicationRequest("svc-z", "z"));
    var created = await post.Content.ReadFromJsonAsync<ApplicationResponse>();

    var get = await client.GetAsync($"/api/v1/catalog/applications/{created!.Id}");
    get.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await get.Content.ReadFromJsonAsync<ApplicationResponse>();
    fetched!.Id.Should().Be(created.Id);
    fetched.Name.Should().Be("svc-z");
}

[Fact]
public async Task GET_by_id_returns_404_for_unknown_id()
{
    var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
    var resp = await client.GetAsync($"/api/v1/catalog/applications/{Guid.NewGuid()}");
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
}
```

- [ ] **Step 5: Run.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --no-build --filter \"FullyQualifiedName~RegisterApplicationTests\" --nologo -v minimal"
```

Expected: 9-10 pass (7-8 from Task 9 + 2 new).

- [ ] **Step 6: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/GetApplicationByIdQuery.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs
git commit --signoff -m "feat(catalog): GET /api/v1/catalog/applications/{id} + integration tests"
```

---

## Task 11: GET `/api/v1/catalog/applications` (list) end-to-end

**Goal:** List all applications in the current tenant.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs` + handler
- Modify: `CatalogEndpointDelegates.cs` (add `ListAsync` delegate)
- Modify: `CatalogModule.cs` (wire endpoint)
- Modify: `RegisterApplicationTests.cs` (add a single-tenant list test)

- [ ] **Step 1: Implement the query + handler.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Application;

public sealed record ListApplicationsQuery();

public sealed class ListApplicationsHandler
{
    public async Task<IReadOnlyList<ApplicationResponse>> Handle(
        ListApplicationsQuery _,
        Kartova.Catalog.Infrastructure.CatalogDbContext db,
        CancellationToken ct)
    {
        var rows = await db.Applications
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(r => r.ToResponse()).ToList();
    }
}
```

- [ ] **Step 2: Add the endpoint delegate and wire it.**

In `CatalogEndpointDelegates.cs`:

```csharp
internal static async Task<IResult> ListApplicationsAsync(
    IMessageBus bus,
    CancellationToken ct)
{
    var rows = await bus.InvokeAsync<IReadOnlyList<ApplicationResponse>>(
        new ListApplicationsQuery(), ct);
    return Results.Ok(rows);
}
```

In `CatalogModule.MapEndpoints`:

```csharp
tenant.MapGet("/applications", CatalogEndpointDelegates.ListApplicationsAsync)
      .WithName("ListApplications");
```

- [ ] **Step 3: Build green.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Add a single-tenant list integration test.** (Cross-tenant list isolation is Task 13.)

```csharp
[Fact]
public async Task GET_list_returns_apps_in_current_tenant_sorted_by_createdAt()
{
    var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");

    var first = await CreateApp(client, "first-app");
    var second = await CreateApp(client, "second-app");

    var resp = await client.GetAsync("/api/v1/catalog/applications");
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await resp.Content.ReadFromJsonAsync<List<ApplicationResponse>>();

    body.Should().NotBeNull();
    body!.Select(x => x.Id).Should().Contain(new[] { first.Id, second.Id });
    body.OrderBy(x => x.CreatedAt).Should().Equal(body);   // already sorted
}

private static async Task<ApplicationResponse> CreateApp(HttpClient c, string name)
{
    var post = await c.PostAsJsonAsync(
        "/api/v1/catalog/applications",
        new RegisterApplicationRequest(name, $"desc for {name}"));
    return (await post.Content.ReadFromJsonAsync<ApplicationResponse>())!;
}
```

Note: this test asserts `Should().Contain(...)` rather than equality because earlier tests in the same suite may have left rows behind (Testcontainers fixtures are typically per-class but shared per-collection). If the fixture wipes rows between tests, change to `Should().HaveCount(2)`. Match whatever the existing Organization integration tests do.

- [ ] **Step 5: Run.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --no-build --filter \"FullyQualifiedName~RegisterApplicationTests\" --nologo -v minimal"
```

Expected: 10-11 pass.

- [ ] **Step 6: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs
git commit --signoff -m "feat(catalog): GET /api/v1/catalog/applications (list) + single-tenant test"
```

---

## Task 12: KeyCloak realm seed — add `admin@orgb` user

**Goal:** Add a second tenant user to the KeyCloak realm seed so cross-tenant integration tests in Task 13 can authenticate as a distinct tenant. No code changes; JSON only.

**Files:**
- Modify: `deploy/keycloak/kartova-realm.json`

- [ ] **Step 1: Inspect the existing seed structure.**

```bash
grep -nE "admin@orga|tenant_id|attributes" deploy/keycloak/kartova-realm.json | head -20
```

Note the JSON shape used for `admin@orga.kartova.local` — same shape will be used for the new user.

- [ ] **Step 2: Add the new user.**

In `deploy/keycloak/kartova-realm.json`, locate the `users` array. After the `admin@orga.kartova.local` entry, add a sibling entry for `admin@orgb.kartova.local` with the **same shape** but:

- different `username`, `email`, `firstName` values (e.g., `admin@orgb.kartova.local`)
- a different `id` (any new GUID — note it for use in step 3 and Task 13)
- the `tenant_id` attribute / role / claim must be **different** from orga's. Use a fresh GUID, e.g., `bbbbbbbb-1111-2222-3333-444444444444`.
- same password `dev_pass` (`temporary: false`)
- same realm-roles assignments **except** any role that's tenant-A-specific.

Example skeleton (adapt to the actual seed structure observed in step 1):

```jsonc
{
  "id": "<new-guid>",
  "username": "admin@orgb.kartova.local",
  "email": "admin@orgb.kartova.local",
  "firstName": "Admin",
  "lastName": "Orgb",
  "enabled": true,
  "emailVerified": true,
  "credentials": [{ "type": "password", "value": "dev_pass", "temporary": false }],
  "attributes": { "tenant_id": ["bbbbbbbb-1111-2222-3333-444444444444"] },
  "realmRoles": ["org-admin"]
}
```

- [ ] **Step 3: Verify by spinning up the stack and obtaining a token.**

```bash
cmd //c "docker compose down -v && docker compose up -d --wait"
```

Then in PowerShell:

```powershell
$body = @{ grant_type='password'; client_id='kartova-api'; username='admin@orgb.kartova.local'; password='dev_pass' }
$tok = Invoke-RestMethod -Method POST -Uri 'http://localhost:8180/realms/kartova/protocol/openid-connect/token' -Body $body -ContentType 'application/x-www-form-urlencoded'
"token len: $($tok.access_token.Length)"
```

Expected: a token (length > 1000). Decode the token (e.g., paste into jwt.io OR run a quick script) and verify the `tenant_id` claim is the new GUID, not orga's.

Tear down:

```bash
cmd //c "docker compose down -v"
```

- [ ] **Step 4: Commit.**

```bash
git add deploy/keycloak/kartova-realm.json
git commit --signoff -m "chore(keycloak): seed admin@orgb.kartova.local with distinct tenant_id"
```

---

## Task 13: Cross-tenant integration tests

**Goal:** Pin the multi-tenant guarantees with the second seeded user. Three scenarios:

1. Tenant B cannot see Tenant A's row via GET-by-id (404, not 403 — no existence leak).
2. Tenant B's GET-list does not include Tenant A's rows.
3. Direct handler invocation cannot persist a row under a different tenant — the scope's tenant always wins over any payload-derived tenant.

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs` (add scenarios 1 + 2)
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CrossTenantWriteTests.cs` (scenario 3)

- [ ] **Step 1: Add cross-tenant GET tests to `RegisterApplicationTests`.**

```csharp
[Fact]
public async Task GET_by_id_returns_404_for_other_tenants_row()
{
    // Tenant A creates a row.
    var clientA = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
    var post = await clientA.PostAsJsonAsync(
        "/api/v1/catalog/applications",
        new RegisterApplicationRequest("orga-private", "owned by orga"));
    var orgaApp = await post.Content.ReadFromJsonAsync<ApplicationResponse>();

    // Tenant B tries to fetch it by id — must 404, not 403, not 200.
    var clientB = await _fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
    var resp = await clientB.GetAsync($"/api/v1/catalog/applications/{orgaApp!.Id}");
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
}

[Fact]
public async Task GET_list_excludes_other_tenants_rows()
{
    var clientA = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
    var orgaApp = await CreateApp(clientA, "orga-isolation-probe");

    var clientB = await _fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
    var resp = await clientB.GetAsync("/api/v1/catalog/applications");
    var body = await resp.Content.ReadFromJsonAsync<List<ApplicationResponse>>();

    body.Should().NotBeNull();
    body!.Select(x => x.Id).Should().NotContain(orgaApp.Id);
    body.All(x => x.TenantId == await _fx.GetTenantIdClaimAsync("admin@orgb.kartova.local"))
        .Should().BeTrue();
}
```

- [ ] **Step 2: Write the cross-tenant write probe.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CrossTenantWriteTests.cs
using FluentAssertions;
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.Catalog.IntegrationTests;

[Collection(KartovaApiCollection.Name)]
public class CrossTenantWriteTests
{
    private readonly KartovaApiFixture _fx;

    public CrossTenantWriteTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Handler_persists_under_scopes_tenant_not_payload_tenant()
    {
        // Drive the handler directly with the scope bound to tenant A — the row must
        // land under tenant A, regardless of any external claim about tenant B.
        await _fx.RunAsTenantAsync("admin@orga.kartova.local", async services =>
        {
            var handler = services.GetRequiredService<RegisterApplicationHandler>();
            var db = services.GetRequiredService<CatalogDbContext>();
            var scope = services.GetRequiredService<ITenantScope>();
            var user = services.GetRequiredService<ICurrentUser>();

            var resp = await handler.Handle(
                new RegisterApplicationCommand("scope-wins", "tenant from scope only"),
                db, scope, user, default);

            resp.TenantId.Should().Be(scope.TenantId.Value);
        });
    }
}
```

`_fx.RunAsTenantAsync(...)` is a helper that opens a DI scope bound to a specific test user's tenant (begin scope, run callback, commit). If it doesn't exist on the fixture, add it — model after how `Kartova.Organization.IntegrationTests` runs handler-level tests with an active tenant scope (slice-2-followup added similar plumbing). If a comparable helper exists under a different name, use that.

- [ ] **Step 3: Run the cross-tenant tests.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --no-build --nologo -v minimal"
```

Expected: 12-14 pass (10-11 from prior tasks + 3 new cross-tenant).

- [ ] **Step 4: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CrossTenantWriteTests.cs
# also add any fixture changes if RunAsTenantAsync was added
git commit --signoff -m "test(catalog): cross-tenant GET-by-id + GET-list + handler write probe"
```

---

## Task 14: Final verification, push, open PR

**Goal:** Confirm Definition of Done, push the branch, open the PR.

- [ ] **Step 1: Full clean build (DoD §1).**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` with `TreatWarningsAsErrors`.

- [ ] **Step 2: Unit + arch tests (DoD §4 part 1).**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: ~82 tests pass:
- SharedKernel: 13 (unchanged)
- SharedKernel.AspNetCore: 19 + 3 (Module routes) + 3 (CurrentUser) = 25
- Organization: 6 (unchanged)
- Catalog: 2 + 9 (Application aggregate) = 11
- ArchitectureTests: 30 + 3 (IModuleRules) = 33

- [ ] **Step 3: Integration tests (DoD §4 part 2).**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName~IntegrationTests\" --nologo -v minimal"
```

Expected: ~30 pass:
- Api.IntegrationTests: 1 (KeyCloak smoke)
- Organization.IntegrationTests: 15
- Catalog.IntegrationTests: 2 + 12-14 (Tasks 9-13)

If counts differ slightly, that's fine — match the actual numbers in the PR body. If anything fails, **stop** and root-cause.

- [ ] **Step 4: Docker compose smoke (DoD §5 — this slice changes HTTP/auth/middleware/pipeline).**

```bash
cmd //c "docker compose up -d --wait"
```

Run the slice-2 + slice-3 acceptance HTTP checks. Capture commands and outputs. In PowerShell:

```powershell
# 1. Anonymous version
$r = Invoke-WebRequest -Uri http://localhost:8080/api/v1/version -UseBasicParsing -SkipHttpErrorCheck
"check 1: HTTP $($r.StatusCode)"

# 2. orgs/me without token
$r = Invoke-WebRequest -Uri http://localhost:8080/api/v1/organizations/me -UseBasicParsing -SkipHttpErrorCheck
"check 2: HTTP $($r.StatusCode)"   # expect 401

# 3. POST admin/organizations without token
$r = Invoke-WebRequest -Method POST -Uri http://localhost:8080/api/v1/admin/organizations -Body '{"name":"x"}' -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck
"check 3: HTTP $($r.StatusCode)"   # expect 401

# 4. tokens
$body = @{ grant_type='password'; client_id='kartova-api'; username='platform-admin@kartova.local'; password='dev_pass' }
$pa = (Invoke-RestMethod -Method POST -Uri 'http://localhost:8180/realms/kartova/protocol/openid-connect/token' -Body $body -ContentType 'application/x-www-form-urlencoded').access_token
$body2 = @{ grant_type='password'; client_id='kartova-api'; username='admin@orga.kartova.local'; password='dev_pass' }
$ou = (Invoke-RestMethod -Method POST -Uri 'http://localhost:8180/realms/kartova/protocol/openid-connect/token' -Body $body2 -ContentType 'application/x-www-form-urlencoded').access_token
$body3 = @{ grant_type='password'; client_id='kartova-api'; username='admin@orgb.kartova.local'; password='dev_pass' }
$ob = (Invoke-RestMethod -Method POST -Uri 'http://localhost:8180/realms/kartova/protocol/openid-connect/token' -Body $body3 -ContentType 'application/x-www-form-urlencoded').access_token
"tokens ok: pa=$($pa.Length) ou=$($ou.Length) ob=$($ob.Length)"

# 5. POST admin/orgs as platform-admin (slice-2 sanity)
$r = Invoke-WebRequest -Method POST -Uri http://localhost:8080/api/v1/admin/organizations -Headers @{Authorization="Bearer $pa"} -Body (@{name="smoke-org"}|ConvertTo-Json) -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck
"check 5: HTTP $($r.StatusCode)"   # expect 201

# 6. POST catalog/applications as admin@orga (slice-3 happy)
$r = Invoke-WebRequest -Method POST -Uri http://localhost:8080/api/v1/catalog/applications -Headers @{Authorization="Bearer $ou"} -Body (@{name="smoke-app";description="smoke"}|ConvertTo-Json) -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck
"check 6: HTTP $($r.StatusCode)"   # expect 201
$smokeApp = $r.Content | ConvertFrom-Json
"created id=$($smokeApp.id)"

# 7. GET catalog/applications/{id} as admin@orga
$r = Invoke-WebRequest -Uri "http://localhost:8080/api/v1/catalog/applications/$($smokeApp.id)" -Headers @{Authorization="Bearer $ou"} -UseBasicParsing -SkipHttpErrorCheck
"check 7: HTTP $($r.StatusCode)"   # expect 200

# 8. GET catalog/applications/{id} as admin@orgb (cross-tenant) — expect 404
$r = Invoke-WebRequest -Uri "http://localhost:8080/api/v1/catalog/applications/$($smokeApp.id)" -Headers @{Authorization="Bearer $ob"} -UseBasicParsing -SkipHttpErrorCheck
"check 8: HTTP $($r.StatusCode)"   # expect 404
```

Expected outputs (capture for the PR body):
- check 1 → 200
- check 2 → 401
- check 3 → 401
- check 5 → 201
- check 6 → 201
- check 7 → 200
- check 8 → 404

If anything else, **stop** and root-cause.

```bash
cmd //c "docker compose down -v"
```

- [ ] **Step 5: Self-review the diff against the spec's success criteria (§10 of spec).**

```bash
cmd //c "git log master..HEAD --oneline"
cmd //c "git diff master..HEAD --stat"
```

Walk the spec's 15 success criteria. Each should map to a commit. Document any gap as a follow-up.

- [ ] **Step 6: Push.**

```bash
git push -u origin feat/slice-3-catalog-application
```

- [ ] **Step 7: Open the PR.**

```bash
gh pr create --base master --head feat/slice-3-catalog-application --title "feat(slice-3): catalog — register Application end-to-end" --body "$(cat <<'EOF'
## Summary

First vertical slice into the Catalog module. Walking-skeleton scope:
register an Application aggregate end-to-end through the new tenant-scope
hybrid filter (slice-2-followup, ADR-0090). No edit, no lifecycle, no UI.

- `IModule.Slug` + `IModuleEndpoints.MapEndpoints` introduced.
  Each module owns its routes; `Program.cs` becomes a uniform foreach.
- `MapTenantScopedModule(slug)` and `MapAdminModule(slug)` helpers in
  `SharedKernel.AspNetCore` enforce the URL convention from ADR-0092.
- `ICurrentUser` accessor over `IHttpContextAccessor` exposes the JWT
  `sub` claim as `Guid UserId`.
- `Application` aggregate with required-field invariants in the factory
  (bundles E-02.F-01.S-05 — required minimum fields enforced as a
  domain rule, not a separate validation pipeline).
- EF migration `AddApplications` creates `catalog_applications` table
  with RLS policy bound to `app.current_tenant_id`.
- Three Wolverine handlers: `RegisterApplicationCommand` /
  `GetApplicationByIdQuery` / `ListApplicationsQuery`.
- Three endpoints: POST + GET-by-id + GET-list under `/api/v1/catalog/applications`.
- KeyCloak realm seed adds `admin@orgb.kartova.local` with a distinct
  `tenant_id` claim to enable cross-tenant integration tests.
- New arch tests (`IModuleRules`) pin: every module declares non-empty
  Slug; Slug matches kebab-case; every IModule also implements IModuleEndpoints.
- `TenantScopeRules` extended to include `CatalogModule` in §6.1.
- Cross-tenant guarantees pinned at three levels: GET-by-id 404,
  GET-list isolation, handler-write probe (scope wins over payload).

Spec: `docs/superpowers/specs/2026-04-29-slice-3-catalog-application-design.md`
Plan: `docs/superpowers/plans/2026-04-29-slice-3-catalog-application-plan.md`
ADR: `ADR-0092` (URL convention, merged separately as PR #9)

## Story coverage

- E-02.F-01.S-01 — Register new application in catalog ✅
- E-02.F-01.S-05 — Required minimum fields enforced ✅ (bundled as domain invariants)

## Verification

- `dotnet build`: 0 warnings, 0 errors with `TreatWarningsAsErrors`
- Unit + arch suite: <fill in actual count>/<fill in> green
- Integration suite: <fill in actual count>/<fill in> green
- Docker compose smoke: 8 HTTP checks (5 slice-1/2 sanity + 3 slice-3
  including cross-tenant 404). All match expected status codes.

## Test plan

- [ ] CI: clean build, all tests green
- [ ] Reviewer pulls branch, runs `docker compose up`, hits
      `POST /api/v1/catalog/applications` with both seeded tenant users
      and confirms cross-tenant 404 on GET-by-id.
- [ ] Reviewer confirms migration creates `catalog_applications` with
      RLS enabled (one policy row in `pg_policy`).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 8: Done.** Mark the spec's §10 success criteria green; the PR is ready for review.

---

## Definition of Done (per CLAUDE.md)

This slice is complete when ALL of the following are green and citable by command + output:

1. ✅ Full solution build with `TreatWarningsAsErrors=true` (Task 14 §1).
2. ✅ Per-task subagent reviews (spec-compliance + code-quality) for each task that ships code — never skipped.
3. ✅ `superpowers:requesting-code-review` invoked at the slice boundary against the full branch diff with spec + plan as context.
4. ✅ Full test suite green: unit + architecture + integration (Task 14 §2-3).
5. ✅ Docker compose smoke with the eight HTTP checks captured (Task 14 §4).

Until all five are green and cited, status is **"implementation staged, verification pending"** — never "slice 3 complete." If any step cannot be run locally (e.g., Docker unavailable), state that explicitly and flag as *pending user verification*.

---

## Self-review (filled in by plan author)

**1. Spec coverage check:**

| Spec section | Tasks |
|---|---|
| §3 decision 1 (walking-skeleton) | 9, 10, 11 |
| §3 decision 2 (URL convention) | ADR-0092 (precursor PR), 1, 3 |
| §3 decision 3 (per-module MapEndpoints) | 1, 3 |
| §3 decision 4 (RequireTenantScope auth only) | 1 (helper enforces); 9, 10, 11 (use helper) |
| §3 decision 5 (owner = JWT sub) | 2 (ICurrentUser); 9 (handler reads it) |
| §3 decision 6 (required-field invariants) | 6 (factory) |
| §3 decision 7 (lifecycle = active, no column) | 6, 8 (no column) |
| §3 decision 8 (tenant from scope only) | 9 (handler reads scope.TenantId); 13 (probe pins it) |
| §3 decision 9 (server-generated id) | 5, 6 |
| §3 decision 10 (RLS on table) | 8 |
| §3 decision 11 (KeyCloak seed admin@orgb) | 12 |
| §4.3 file map | every "Created" row has a task; every "Modified" row has a task |
| §5 components | 1 (helpers), 2 (ICurrentUser), 3 (IModule), 5/6 (Application), 7 (DTOs), 8 (EF), 9-11 (CQRS) |
| §6 data flow | exercised end-to-end by 9-11 + 14 §4 |
| §7 error handling | 9 (400 via factory), 10 (404 via Results.Problem), inherits the rest |
| §8 testing | unit (6), arch (4), integration (9, 10, 11, 13), KeyCloak seed (12), docker smoke (14 §4) |
| §10 success criteria 1-15 | task-by-task in §self-review row above |
| §13 follow-ups | not in plan (registered in spec for future) |

No gaps.

**2. Placeholder scan:** No "TBD" / "TODO" / "implement later" tokens. Sub-step instructions like "match whatever the existing Organization integration tests do" (Task 11) and "use whatever helper exists under a different name" (Task 13) are not placeholders — they are deferred decisions for the implementer to resolve by inspection of existing code, not future-self promises.

**3. Type / signature consistency:**

- `IModule.Slug` (`string`) — declared Task 3, asserted Task 4, used Task 9-11 indirectly through `MapTenantScopedModule(Slug)`.
- `IModuleEndpoints.MapEndpoints(IEndpointRouteBuilder)` — declared Task 3, used in Tasks 3, 9, 10, 11; asserted Task 4.
- `ICurrentUser.UserId` (`Guid`) — declared Task 2, used Task 9 (handler).
- `Application.Create(string name, string description, Guid ownerUserId, TenantId tenantId)` — declared Task 6, used Task 9 (handler).
- `ApplicationResponse(Guid Id, Guid TenantId, string Name, string Description, Guid OwnerUserId, DateTimeOffset CreatedAt)` — declared Task 7, returned by Tasks 9-11.
- `RegisterApplicationCommand(string Name, string Description)` — declared Task 9, no other consumer.
- `GetApplicationByIdQuery(Guid Id)` — declared Task 10, no other consumer.
- `ListApplicationsQuery()` — declared Task 11, no other consumer.
- `MapTenantScopedModule(this IEndpointRouteBuilder, string)` returns `RouteGroupBuilder` — declared Task 1, used Tasks 3, 9.
- `MapAdminModule(this IEndpointRouteBuilder, string)` returns `RouteGroupBuilder` — declared Task 1, used Task 3 (Organization admin route).

No mismatches.

**4. Scope check:** 14 tasks, all single-PR-sized. Each task self-contained with its own commit. Plan ships in one PR; ADR-0092 ships separately as the precursor.

**No issues found.**
