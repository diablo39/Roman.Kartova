# Walking Skeleton Implementation Plan (Phase 0, Slice 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an end-to-end vertical slice of the Kartova stack (modular monolith + Wolverine + migrator + docker-compose + frontend shell + CI + minimal Helm) that proves every load-bearing ADR is correctly wired, without implementing any domain feature.

**Architecture:** Scaffold a single `Kartova.slnx` with `SharedKernel`, composition-root `Api`, a migrator console, and one canary `Catalog` module using per-module Clean Architecture (`Domain` / `Application` / `Infrastructure` / `Contracts`). Tests are co-located per module in `src/Modules/{Module}/` with cross-cutting `ArchitectureTests` in `tests/`. The frontend (`web/`) uses Vite + React 19 + TypeScript strict + Tailwind v4 + shadcn/ui to render a static shell with a placeholder route. Infrastructure is wired via Docker Compose (`postgres` + `migrator` + `api`) and a minimal Helm chart skeleton (`deploy/helm/kartova/`). GitHub Actions runs architecture + unit + integration tests on every push.

**Tech Stack:** .NET 10 LTS (ASP.NET Core + EF Core 10 + Wolverine 3.x) · PostgreSQL 16 · React 19 + TypeScript 5 strict + Vite 6 + Tailwind CSS v4 + shadcn/ui + Radix · Docker Compose · Helm 3 · GitHub Actions · xUnit + FluentAssertions + Testcontainers.PostgreSql · NetArchTest.Rules

**Pre-execution flags (confirm before running plan):**

- [ ] **Container registry path:** plan uses `ghcr.io/romanglogowski/kartova-{api,migrator,web}`. Confirm GitHub handle is `romanglogowski` (or substitute real handle before Task 15 onward).
- [ ] **Dev DB roles:** plan creates two PostgreSQL roles in `postgres/init.sql`: `migrator` (DDL rights, password `dev`) and `kartova_app` (DML only, password `dev`). Both are local-dev-only; confirmed non-secret. Production roles come from Secrets in Slice 4.
- [ ] **Postgres database name:** `kartova` (default).
- [ ] **Package versions will be resolved at `dotnet restore` / `npm install` time** — plan uses `.*` wildcards for WolverineFx.* and similar rapidly-evolving packages. If reproducibility is critical, run `dotnet list package` after restore and pin in a follow-up commit.

---

## Task 1: Repository root scaffolding

**Goal:** Create root-level config files so the tooling (dotnet SDK pin, .gitignore, editor conventions, Makefile, README) is in place before any code is written.

**Files:**
- Create: `global.json`
- Create: `.editorconfig`
- Modify: `.gitignore` (append additions)
- Create: `Makefile`
- Create: `README.md`

- [ ] **Step 1: Create `global.json`**

File: `global.json`

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 2: Create `.editorconfig`**

File: `.editorconfig`

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
indent_style = space
indent_size = 4
trim_trailing_whitespace = true

[*.{json,yml,yaml,csproj,props,targets,xml}]
indent_size = 2

[*.{ts,tsx,js,jsx,css,html,md}]
indent_size = 2

[*.cs]
dotnet_sort_system_directives_first = true
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
```

- [ ] **Step 3: Append to `.gitignore`**

Append to end of `.gitignore`:

```gitignore

# Kartova project additions (Slice 1)
node_modules/
web/dist/
web/.vite/
TestResults/
coverage/
.vs/
.idea/
*.DotSettings.user
.env.local
.env.*.local
```

- [ ] **Step 4: Create `Makefile`**

File: `Makefile`

```makefile
.PHONY: up down rebuild test archtest web logs clean

# Start backend services (postgres + migrator + api)
up:
	docker compose up -d postgres
	docker compose up migrator
	docker compose up -d api
	@echo ""
	@echo "API running at http://localhost:8080"
	@echo "Health: curl http://localhost:8080/health/ready"
	@echo "Version: curl http://localhost:8080/api/v1/version"

# Stop and remove all services + volumes
down:
	docker compose down -v

# Rebuild images and restart
rebuild:
	docker compose build --no-cache
	$(MAKE) down
	$(MAKE) up

# Run full test suite (arch + unit + integration)
test:
	cmd /c dotnet test Kartova.slnx --configuration Release

# Run only architecture tests (fast fail-early gate)
archtest:
	cmd /c dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --configuration Release

# Start frontend dev server
web:
	cd web && npm run dev

# Tail logs of all services
logs:
	docker compose logs -f

# Full cleanup: down + remove build artifacts
clean: down
	cmd /c dotnet clean Kartova.slnx
	rm -rf web/node_modules web/dist
```

- [ ] **Step 5: Create `README.md` stub**

File: `README.md`

```markdown
# Kartova

SaaS service catalog and developer portal (Backstage + Compass + Statuspage hybrid).

## Stack

.NET 10 LTS · React 19 · PostgreSQL 16 · Wolverine · KafkaFlow · Kubernetes.

See [CLAUDE.md](CLAUDE.md) for full architecture, key decisions, and working agreements.

## Local development

Prerequisites: Docker Desktop (or equivalent), .NET 10 SDK, Node 20 LTS, `make`.

```bash
make up        # start postgres + migrator + api
make web       # start frontend dev server (in a second terminal)
make test      # run full test suite
make archtest  # run architecture tests only (fast)
make down      # stop everything, remove volumes
```

## Documentation

- [ADR library](docs/architecture/decisions/README.md) — 88 accepted decisions with keyword index
- [Product requirements](docs/product/PRODUCT-REQUIREMENTS.md)
- [Backlog](docs/product/EPICS-AND-STORIES.md) — 30 epics, 73 features, 209 stories
- [Progress](docs/product/CHECKLIST.md)
- [Design system](docs/design/DESIGN.md)

## Repository layout

See `CLAUDE.md` "Where to find things" section and
[ADR-0082](docs/architecture/decisions/ADR-0082-modular-monolith-architecture.md) for the modular monolith structure.
```

- [ ] **Step 6: Verify files exist**

Run:
```bash
ls -la global.json .editorconfig Makefile README.md .gitignore
```

Expected: all five files listed.

- [ ] **Step 7: Commit**

```bash
git add global.json .editorconfig .gitignore Makefile README.md
git commit -m "chore: Add repository root scaffolding (global.json, Makefile, README, editor/gitignore config)"
```

---

## Task 2: Create empty `Kartova.slnx`

**Goal:** Empty solution file so all subsequent `dotnet new` commands can add projects to it.

**Files:**
- Create: `Kartova.slnx`

- [ ] **Step 1: Create empty solution**

Run (Windows):
```bash
cmd /c dotnet new sln --name Kartova --output .
```

Expected output:
```
The template "Solution File" was created successfully.
```

- [ ] **Step 2: Verify solution file**

Run:
```bash
ls Kartova.slnx
cat Kartova.slnx | head -5
```

Expected: file exists and contains XML (`.slnx` format, default in .NET 10). First line should be the XML root: `<Solution>` or similar. The classic `Microsoft Visual Studio Solution File, Format Version 12.00` header is **not** expected — that's the old `.sln` format, which Kartova does not use (see ADR-0082 Implementation Notes).

- [ ] **Step 3: Commit**

```bash
git add Kartova.slnx
git commit -m "chore: Add empty Kartova.slnx"
```

---

## Task 3: `Kartova.SharedKernel` project

**Goal:** Cross-module shared primitives — `TenantId` value object and `DomainEvent` base type. No external dependencies beyond BCL.

**Files:**
- Create: `src/Kartova.SharedKernel/Kartova.SharedKernel.csproj`
- Create: `src/Kartova.SharedKernel/TenantId.cs`
- Create: `src/Kartova.SharedKernel/DomainEvent.cs`

- [ ] **Step 1: Create project**

Run:
```bash
cmd /c dotnet new classlib --name Kartova.SharedKernel --output src/Kartova.SharedKernel --framework net10.0
cmd /c dotnet sln Kartova.slnx add src/Kartova.SharedKernel/Kartova.SharedKernel.csproj
```

- [ ] **Step 2: Remove default `Class1.cs`**

```bash
rm src/Kartova.SharedKernel/Class1.cs
```

- [ ] **Step 3: Set `TreatWarningsAsErrors` and nullable**

File: `src/Kartova.SharedKernel/Kartova.SharedKernel.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

</Project>
```

- [ ] **Step 4: Create `TenantId.cs`**

File: `src/Kartova.SharedKernel/TenantId.cs`

```csharp
namespace Kartova.SharedKernel;

/// <summary>
/// Strongly-typed tenant identifier. Used as a filter key for multi-tenancy
/// (PostgreSQL RLS, Elasticsearch tenant routing). Immutable value object.
/// </summary>
public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());

    public static TenantId Parse(string value)
    {
        if (!Guid.TryParse(value, out var guid))
        {
            throw new FormatException($"Invalid TenantId: '{value}'");
        }
        return new TenantId(guid);
    }

    public override string ToString() => Value.ToString();
}
```

- [ ] **Step 5: Create `DomainEvent.cs`**

File: `src/Kartova.SharedKernel/DomainEvent.cs`

```csharp
namespace Kartova.SharedKernel;

/// <summary>
/// Base type for domain events. Concrete events are sealed records.
/// </summary>
/// <remarks>
/// Enforced by architecture tests (ADR-0083).
/// </remarks>
public abstract record DomainEvent(DateTimeOffset OccurredAt)
{
    protected DomainEvent() : this(DateTimeOffset.UtcNow) { }
}
```

- [ ] **Step 6: Build**

```bash
cmd /c dotnet build src/Kartova.SharedKernel/Kartova.SharedKernel.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 7: Commit**

```bash
git add src/Kartova.SharedKernel/ Kartova.slnx
git commit -m "feat(sharedkernel): Add TenantId value object and DomainEvent base type"
```

---

## Task 4: Catalog module — `Domain`, `Application`, `Contracts` csprojs with marker types

**Goal:** Three empty-of-logic csprojs for Catalog module with correct project references enforcing Clean Architecture layering.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/Kartova.Catalog.Domain.csproj`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/CatalogDomainMarker.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/Kartova.Catalog.Application.csproj`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/CatalogApplicationMarker.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/Kartova.Catalog.Contracts.csproj`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/CatalogContractsMarker.cs`

- [ ] **Step 1: Create Domain project**

```bash
cmd /c dotnet new classlib --name Kartova.Catalog.Domain --output src/Modules/Catalog/Kartova.Catalog.Domain --framework net10.0
cmd /c dotnet sln Kartova.slnx add src/Modules/Catalog/Kartova.Catalog.Domain/Kartova.Catalog.Domain.csproj
rm src/Modules/Catalog/Kartova.Catalog.Domain/Class1.cs
```

- [ ] **Step 2: Configure Domain csproj**

File: `src/Modules/Catalog/Kartova.Catalog.Domain/Kartova.Catalog.Domain.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create Domain marker**

File: `src/Modules/Catalog/Kartova.Catalog.Domain/CatalogDomainMarker.cs`

```csharp
namespace Kartova.Catalog.Domain;

/// <summary>
/// Assembly anchor for NetArchTest and reflection-based module discovery.
/// Intentionally empty — concrete domain types arrive in Slice 3.
/// </summary>
public static class CatalogDomainMarker { }
```

- [ ] **Step 4: Create Application project**

```bash
cmd /c dotnet new classlib --name Kartova.Catalog.Application --output src/Modules/Catalog/Kartova.Catalog.Application --framework net10.0
cmd /c dotnet sln Kartova.slnx add src/Modules/Catalog/Kartova.Catalog.Application/Kartova.Catalog.Application.csproj
rm src/Modules/Catalog/Kartova.Catalog.Application/Class1.cs
```

- [ ] **Step 5: Configure Application csproj**

File: `src/Modules/Catalog/Kartova.Catalog.Application/Kartova.Catalog.Application.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.Catalog.Domain\Kartova.Catalog.Domain.csproj" />
    <ProjectReference Include="..\Kartova.Catalog.Contracts\Kartova.Catalog.Contracts.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 6: Create Application marker**

File: `src/Modules/Catalog/Kartova.Catalog.Application/CatalogApplicationMarker.cs`

```csharp
namespace Kartova.Catalog.Application;

/// <summary>
/// Assembly anchor. Handlers and use cases arrive in Slice 3.
/// </summary>
public static class CatalogApplicationMarker { }
```

- [ ] **Step 7: Create Contracts project**

```bash
cmd /c dotnet new classlib --name Kartova.Catalog.Contracts --output src/Modules/Catalog/Kartova.Catalog.Contracts --framework net10.0
cmd /c dotnet sln Kartova.slnx add src/Modules/Catalog/Kartova.Catalog.Contracts/Kartova.Catalog.Contracts.csproj
rm src/Modules/Catalog/Kartova.Catalog.Contracts/Class1.cs
```

- [ ] **Step 8: Configure Contracts csproj**

File: `src/Modules/Catalog/Kartova.Catalog.Contracts/Kartova.Catalog.Contracts.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 9: Create Contracts marker**

File: `src/Modules/Catalog/Kartova.Catalog.Contracts/CatalogContractsMarker.cs`

```csharp
namespace Kartova.Catalog.Contracts;

/// <summary>
/// Public integration surface. Commands, queries, and events that cross module
/// boundaries will live here. Empty in Slice 1.
/// </summary>
public static class CatalogContractsMarker { }
```

- [ ] **Step 10: Build all three**

```bash
cmd /c dotnet build src/Modules/Catalog/Kartova.Catalog.Domain/Kartova.Catalog.Domain.csproj
cmd /c dotnet build src/Modules/Catalog/Kartova.Catalog.Application/Kartova.Catalog.Application.csproj
cmd /c dotnet build src/Modules/Catalog/Kartova.Catalog.Contracts/Kartova.Catalog.Contracts.csproj
```

Expected: three `Build succeeded` messages.

- [ ] **Step 11: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/ src/Modules/Catalog/Kartova.Catalog.Application/ src/Modules/Catalog/Kartova.Catalog.Contracts/ Kartova.slnx
git commit -m "feat(catalog): Add Domain, Application, Contracts csproj skeletons with marker types"
```

---

## Task 5: `Kartova.Catalog.Infrastructure` with `CatalogDbContext` and `KartovaMetadata` entity

**Goal:** Infrastructure layer for Catalog module containing EF Core DbContext and a single technical entity `__kartova_metadata` proving the DB layer is wired.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/KartovaMetadata.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContextFactory.cs`

- [ ] **Step 1: Create Infrastructure project**

```bash
cmd /c dotnet new classlib --name Kartova.Catalog.Infrastructure --output src/Modules/Catalog/Kartova.Catalog.Infrastructure --framework net10.0
cmd /c dotnet sln Kartova.slnx add src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj
rm src/Modules/Catalog/Kartova.Catalog.Infrastructure/Class1.cs
```

- [ ] **Step 2: Add EF Core + Npgsql + Microsoft.Extensions.DependencyInjection package references**

Run from repo root:
```bash
cd src/Modules/Catalog/Kartova.Catalog.Infrastructure
cmd /c dotnet add package Microsoft.EntityFrameworkCore --version 10.0.0
cmd /c dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.0
cmd /c dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.0
cmd /c dotnet add package Microsoft.Extensions.DependencyInjection.Abstractions --version 10.0.0
cd ../../../..
```

> If `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0` is not yet published at execution time, substitute the latest `10.x.*` preview or the newest `9.x` compatible version and note in the commit message.

- [ ] **Step 3: Configure Infrastructure csproj**

File: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.Catalog.Application\Kartova.Catalog.Application.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Create `KartovaMetadata.cs`**

File: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/KartovaMetadata.cs`

```csharp
namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Technical table proving the migration pipeline works end-to-end.
/// One row per module; schema_version incremented when that module's
/// migration set changes. Read/written only by the migrator — not by the API.
/// </summary>
internal sealed class KartovaMetadata
{
    public string ModuleName { get; set; } = default!;
    public int SchemaVersion { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}
```

- [ ] **Step 5: Create `CatalogDbContext.cs`**

File: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// EF Core context for the Catalog module. Owns <see cref="KartovaMetadata"/>
/// in Slice 1. Domain entities (Service, Application, API, etc.) arrive in Slice 3.
/// </summary>
public sealed class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }

    internal DbSet<KartovaMetadata> Metadata => Set<KartovaMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KartovaMetadata>(entity =>
        {
            entity.ToTable("__kartova_metadata");
            entity.HasKey(m => m.ModuleName);

            entity.Property(m => m.ModuleName)
                .HasColumnName("module_name")
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(m => m.SchemaVersion)
                .HasColumnName("schema_version")
                .IsRequired();

            entity.Property(m => m.AppliedAt)
                .HasColumnName("applied_at")
                .IsRequired();
        });
    }
}
```

- [ ] **Step 6: Create design-time factory for EF Core tooling**

File: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContextFactory.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Enables `dotnet ef migrations add` without a running host.
/// Production connection strings come from IModule.RegisterServices.
/// </summary>
internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=kartova_design;Username=migrator;Password=dev",
                npg => npg.MigrationsAssembly(typeof(CatalogDbContextFactory).Assembly.FullName))
            .Options;

        return new CatalogDbContext(options);
    }
}
```

- [ ] **Step 7: Build**

```bash
cmd /c dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj
```

Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/ Kartova.slnx
git commit -m "feat(catalog): Add Infrastructure csproj with CatalogDbContext and __kartova_metadata entity"
```

---

## Task 6: `IModule` interface and `CatalogModule` implementation

**Goal:** Module registration pattern (ADR-0082) so the composition root in `Kartova.Api` can enumerate and bootstrap each module uniformly.

**Files:**
- Create: `src/Kartova.Api/Kartova.Api.csproj` (will be expanded in Task 8)
- Create: `src/Kartova.Api/Modules/IModule.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`

- [ ] **Step 1: Create Api project scaffolding (we'll fill Program.cs in Task 8)**

```bash
cmd /c dotnet new web --name Kartova.Api --output src/Kartova.Api --framework net10.0
cmd /c dotnet sln Kartova.slnx add src/Kartova.Api/Kartova.Api.csproj
```

This creates a default `Program.cs` we'll overwrite in Task 8 — ignore its content for now.

- [ ] **Step 2: Set Api csproj properties**

Edit `src/Kartova.Api/Kartova.Api.csproj` — add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<Nullable>enable</Nullable>`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <UserSecretsId>kartova-api-dev</UserSecretsId>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Create `IModule` interface**

File: `src/Kartova.Api/Modules/IModule.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Kartova.Api.Modules;

/// <summary>
/// Every bounded-context module implements this interface. The composition
/// root in <see cref="Program"/> enumerates all modules and invokes them
/// in order. Enforced by NetArchTest boundary rules (ADR-0082).
/// </summary>
public interface IModule
{
    /// <summary>Stable short name, used for logging, health checks, metadata.</summary>
    string Name { get; }

    /// <summary>Registers DbContext, repositories, and other module-local services.</summary>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Configures Wolverine for this module (handlers discovery, publish routes).</summary>
    void ConfigureWolverine(WolverineOptions options);
}
```

- [ ] **Step 4: Add Wolverine reference to Api (so IModule compiles)**

```bash
cd src/Kartova.Api
cmd /c dotnet add package WolverineFx --version 3.0.0
cd ../..
```

- [ ] **Step 5: Add Kartova.Api reference from Catalog.Infrastructure (for IModule)**

> **Decision flag:** Catalog.Infrastructure referencing Kartova.Api creates an inverted dependency. Better: move `IModule` into `Kartova.SharedKernel` so all modules can implement it without referencing the composition root. Refactor in this step.

Delete `src/Kartova.Api/Modules/IModule.cs` and recreate it in SharedKernel:

File: `src/Kartova.SharedKernel/IModule.cs`

```csharp
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
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    void ConfigureWolverine(WolverineOptions options);
}
```

Add Wolverine + DI abstractions to SharedKernel csproj.

File: `src/Kartova.SharedKernel/Kartova.SharedKernel.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="WolverineFx" Version="3.0.0" />
  </ItemGroup>

</Project>
```

Remove the empty `Modules/` folder from `Kartova.Api` (if created):

```bash
rm -rf src/Kartova.Api/Modules
```

- [ ] **Step 6: Create `CatalogModule.cs`**

File: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`

```csharp
using Kartova.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Kartova.Catalog.Infrastructure;

public sealed class CatalogModule : IModule
{
    public string Name => "catalog";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Kartova")
            ?? throw new InvalidOperationException(
                "Connection string 'Kartova' is required. Set it via ConnectionStrings__Kartova env var.");

        services.AddDbContext<CatalogDbContext>(opts =>
            opts.UseNpgsql(connectionString, npg =>
                npg.MigrationsAssembly(typeof(CatalogDbContext).Assembly.FullName)));
    }

    public void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(CatalogModule).Assembly);
        // Handlers and publish routes arrive in Slice 3.
    }
}
```

- [ ] **Step 7: Add SharedKernel reference + Wolverine to Infrastructure csproj**

File: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj`

Add after the existing ItemGroups:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>
```

The SharedKernel already has Wolverine as a transitive dependency, so no additional PackageReference is needed here.

- [ ] **Step 8: Build solution**

```bash
cmd /c dotnet build Kartova.slnx
```

Expected: all projects build, 0 warnings, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/ Kartova.slnx
git commit -m "feat(modules): Add IModule interface in SharedKernel and CatalogModule implementation"
```

---

## Task 7: Initial EF Core migration

**Goal:** One EF Core migration that creates `__kartova_metadata` and seeds `('catalog', 1, NOW())`. Proves the migrator pipeline end-to-end.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/20260421_InitialCatalog.cs` (generated)
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/20260421_InitialCatalog.Designer.cs` (generated)
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/CatalogDbContextModelSnapshot.cs` (generated)

- [ ] **Step 1: Install `dotnet-ef` tool if not present**

```bash
cmd /c dotnet tool install --global dotnet-ef --version 10.0.0 || cmd /c dotnet tool update --global dotnet-ef --version 10.0.0
```

- [ ] **Step 2: Generate initial migration**

```bash
cmd /c dotnet ef migrations add InitialCatalog ^
  --project src/Modules/Catalog/Kartova.Catalog.Infrastructure ^
  --startup-project src/Modules/Catalog/Kartova.Catalog.Infrastructure ^
  --context CatalogDbContext ^
  --output-dir Migrations
```

Expected: three files created in `Migrations/`, build succeeds.

- [ ] **Step 3: Rename migration file to include date prefix**

```bash
cd src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations
# Files generated with timestamp prefix already (e.g., 20260421120000_InitialCatalog.cs).
# Confirm listing:
ls
cd ../../../../..
```

- [ ] **Step 4: Open generated migration and add seed INSERT**

Edit the generated `<timestamp>_InitialCatalog.cs`. The `Up` method will contain the `CreateTable` for `__kartova_metadata`. **Append** a seed insert at the end of `Up`:

```csharp
migrationBuilder.Sql("""
    INSERT INTO __kartova_metadata (module_name, schema_version, applied_at)
    VALUES ('catalog', 1, NOW())
    ON CONFLICT (module_name) DO NOTHING;
""");
```

And **prepend** to `Down`:

```csharp
migrationBuilder.Sql("DELETE FROM __kartova_metadata WHERE module_name = 'catalog';");
```

- [ ] **Step 5: Build**

```bash
cmd /c dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/
git commit -m "feat(catalog): Add InitialCatalog migration creating __kartova_metadata with seed row"
```

---

## Task 8: `Kartova.Api` `Program.cs` with HealthChecks, /version, Wolverine, module registration

**Goal:** Composition root wired for three health probes (ADR-0060), `/api/v1/version` endpoint, Wolverine bootstrap with PostgreSQL persistence (no handlers), and `CatalogModule` registration.

**Files:**
- Modify: `src/Kartova.Api/Kartova.Api.csproj` (add packages)
- Replace: `src/Kartova.Api/Program.cs`
- Create: `src/Kartova.Api/appsettings.json`
- Create: `src/Kartova.Api/appsettings.Development.json`

- [ ] **Step 1: Add package references to Api csproj**

```bash
cd src/Kartova.Api
cmd /c dotnet add package WolverineFx.EntityFrameworkCore --version 3.0.0
cmd /c dotnet add package WolverineFx.Postgresql --version 3.0.0
cmd /c dotnet add package Microsoft.AspNetCore.Diagnostics.HealthChecks --version 10.0.0
cmd /c dotnet add package AspNetCore.HealthChecks.NpgSql --version 10.0.0
cmd /c dotnet add reference ../Kartova.SharedKernel/Kartova.SharedKernel.csproj
cmd /c dotnet add reference ../Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj
cd ../..
```

- [ ] **Step 2: Verify csproj content**

File: `src/Kartova.Api/Kartova.Api.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <UserSecretsId>kartova-api-dev</UserSecretsId>
    <DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="WolverineFx" Version="3.0.0" />
    <PackageReference Include="WolverineFx.EntityFrameworkCore" Version="3.0.0" />
    <PackageReference Include="WolverineFx.Postgresql" Version="3.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Diagnostics.HealthChecks" Version="10.0.0" />
    <PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
    <ProjectReference Include="..\Modules\Catalog\Kartova.Catalog.Infrastructure\Kartova.Catalog.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Replace `Program.cs`**

File: `src/Kartova.Api/Program.cs`

```csharp
using System.Reflection;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Oakton;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

// Module registry — explicit list; Slice 1 has only Catalog.
IModule[] modules =
[
    new CatalogModule(),
];

// Register each module's services.
foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

var kartovaConnection = builder.Configuration.GetConnectionString("Kartova")
    ?? throw new InvalidOperationException("ConnectionStrings__Kartova missing");

// Wolverine bootstrap — persistence only, no handlers or Kafka routing yet.
builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(kartovaConnection, schemaName: "wolverine");

    foreach (var module in modules)
    {
        module.ConfigureWolverine(opts);
    }
});

// Health checks — three probes per ADR-0060.
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(kartovaConnection, name: "postgres", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.MapGet("/api/v1/version", () =>
{
    var assembly = Assembly.GetExecutingAssembly();
    var version = assembly.GetName().Version?.ToString() ?? "0.1.0";
    var informationalVersion = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;
    var commit = Environment.GetEnvironmentVariable("GIT_COMMIT") ?? "unknown";
    var buildTime = Environment.GetEnvironmentVariable("BUILD_TIME") ?? DateTimeOffset.UtcNow.ToString("O");

    return Results.Ok(new
    {
        version = informationalVersion ?? version,
        commit,
        buildTime,
    });
});

return await app.RunOaktonCommands(args);
```

> Oakton is required for `UseWolverine()`'s `RunOaktonCommands`. Add it:

```bash
cd src/Kartova.Api
cmd /c dotnet add package Oakton --version 7.1.0
cd ../..
```

- [ ] **Step 4: Create `appsettings.json`**

File: `src/Kartova.Api/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Kartova": ""
  }
}
```

- [ ] **Step 5: Create `appsettings.Development.json`**

File: `src/Kartova.Api/appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "ConnectionStrings": {
    "Kartova": "Host=localhost;Port=5432;Database=kartova;Username=kartova_app;Password=dev"
  }
}
```

- [ ] **Step 6: Delete default `launchSettings.json` artifacts we don't need**

No action — the template-generated `Properties/launchSettings.json` is fine for local debug. Leave it as-is.

- [ ] **Step 7: Build**

```bash
cmd /c dotnet build src/Kartova.Api/Kartova.Api.csproj
```

Expected: build succeeds, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add src/Kartova.Api/
git commit -m "feat(api): Wire Program.cs with Wolverine persistence, three health probes, /api/v1/version endpoint, Catalog module registration"
```

---

## Task 9: `Kartova.Migrator` console app

**Goal:** Console app that enumerates `IModule[]`, resolves each module's `DbContext`, calls `Database.MigrateAsync()`, exits 0 on success.

**Files:**
- Create: `src/Kartova.Migrator/Kartova.Migrator.csproj`
- Create: `src/Kartova.Migrator/Program.cs`
- Create: `src/Kartova.Migrator/appsettings.json`

- [ ] **Step 1: Create console project**

```bash
cmd /c dotnet new console --name Kartova.Migrator --output src/Kartova.Migrator --framework net10.0
cmd /c dotnet sln Kartova.slnx add src/Kartova.Migrator/Kartova.Migrator.csproj
```

- [ ] **Step 2: Add references**

```bash
cd src/Kartova.Migrator
cmd /c dotnet add package Microsoft.Extensions.Hosting --version 10.0.0
cmd /c dotnet add package Microsoft.Extensions.Configuration.Json --version 10.0.0
cmd /c dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables --version 10.0.0
cmd /c dotnet add reference ../Kartova.SharedKernel/Kartova.SharedKernel.csproj
cmd /c dotnet add reference ../Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj
cd ../..
```

- [ ] **Step 3: Configure csproj**

File: `src/Kartova.Migrator/Kartova.Migrator.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
    <ProjectReference Include="..\Modules\Catalog\Kartova.Catalog.Infrastructure\Kartova.Catalog.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Replace `Program.cs`**

File: `src/Kartova.Migrator/Program.cs`

```csharp
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);

IModule[] modules =
[
    new CatalogModule(),
];

foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

// The migrator doesn't route Kafka messages, but Wolverine may want its own tables
// (outbox persistence) — we still register schema so migrations include them in Slice 3.
// For Slice 1 we skip Wolverine bootstrap in the migrator itself; wolverine tables are
// created lazily by the API.

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Kartova migrator starting; {ModuleCount} module(s) registered.", modules.Length);

foreach (var module in modules)
{
    using var scope = host.Services.CreateScope();
    logger.LogInformation("Applying migrations for module '{Module}'...", module.Name);

    // Each module's DbContext is registered via IModule.RegisterServices.
    // We locate it by naming convention: {Module}DbContext in the module's Infrastructure assembly.
    var dbContext = scope.ServiceProvider.GetService<CatalogDbContext>()
        ?? throw new InvalidOperationException(
            $"DbContext for module '{module.Name}' not registered.");

    await dbContext.Database.MigrateAsync();
    logger.LogInformation("Module '{Module}' migrated.", module.Name);
}

logger.LogInformation("All migrations applied. Exiting.");
return 0;
```

> **Note:** Slice 1 uses an explicit `GetService<CatalogDbContext>()`. Slice 3+ will introduce a generic mechanism (e.g., `IMigrationTarget` abstraction) once multiple modules exist. Flagged in the plan; not blocking.

- [ ] **Step 5: Create `appsettings.json` for migrator**

File: `src/Kartova.Migrator/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.EntityFrameworkCore.Migrations": "Information"
    }
  },
  "ConnectionStrings": {
    "Kartova": ""
  }
}
```

- [ ] **Step 6: Build**

```bash
cmd /c dotnet build src/Kartova.Migrator/Kartova.Migrator.csproj
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/Kartova.Migrator/ Kartova.slnx
git commit -m "feat(migrator): Add Kartova.Migrator console that applies migrations for each registered module"
```

---

## Task 10: `tests/Kartova.ArchitectureTests` — 3 NetArchTest rules

**Goal:** Architecture tests as CI gate (ADR-0083). Three initial rules: Clean Architecture layering, module boundary, forbidden dependencies.

**Files:**
- Create: `tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj`
- Create: `tests/Kartova.ArchitectureTests/CleanArchitectureLayerTests.cs`
- Create: `tests/Kartova.ArchitectureTests/ModuleBoundaryTests.cs`
- Create: `tests/Kartova.ArchitectureTests/ForbiddenDependencyTests.cs`
- Create: `tests/Kartova.ArchitectureTests/AssemblyRegistry.cs`

- [ ] **Step 1: Create xUnit project**

```bash
cmd /c dotnet new xunit --name Kartova.ArchitectureTests --output tests/Kartova.ArchitectureTests --framework net10.0
cmd /c dotnet sln Kartova.slnx add tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj
rm tests/Kartova.ArchitectureTests/UnitTest1.cs
```

- [ ] **Step 2: Add package + project references**

```bash
cd tests/Kartova.ArchitectureTests
cmd /c dotnet add package NetArchTest.Rules --version 1.3.2
cmd /c dotnet add package FluentAssertions --version 6.12.0
cmd /c dotnet add reference ../../src/Kartova.SharedKernel/Kartova.SharedKernel.csproj
cmd /c dotnet add reference ../../src/Kartova.Api/Kartova.Api.csproj
cmd /c dotnet add reference ../../src/Modules/Catalog/Kartova.Catalog.Domain/Kartova.Catalog.Domain.csproj
cmd /c dotnet add reference ../../src/Modules/Catalog/Kartova.Catalog.Application/Kartova.Catalog.Application.csproj
cmd /c dotnet add reference ../../src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj
cmd /c dotnet add reference ../../src/Modules/Catalog/Kartova.Catalog.Contracts/Kartova.Catalog.Contracts.csproj
cd ../..
```

- [ ] **Step 3: Create `AssemblyRegistry.cs`**

File: `tests/Kartova.ArchitectureTests/AssemblyRegistry.cs`

```csharp
using System.Reflection;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Central registry of all production assemblies — updated whenever a new module is added.
/// </summary>
internal static class AssemblyRegistry
{
    public static readonly Assembly SharedKernel = typeof(TenantId).Assembly;
    public static readonly Assembly Api = typeof(Program).Assembly;

    public static class Catalog
    {
        public static readonly Assembly Domain = typeof(CatalogDomainMarker).Assembly;
        public static readonly Assembly Application = typeof(CatalogApplicationMarker).Assembly;
        public static readonly Assembly Infrastructure = typeof(CatalogModule).Assembly;
        public static readonly Assembly Contracts = typeof(CatalogContractsMarker).Assembly;
    }

    public static IEnumerable<Assembly> AllProduction()
    {
        yield return SharedKernel;
        yield return Api;
        yield return Catalog.Domain;
        yield return Catalog.Application;
        yield return Catalog.Infrastructure;
        yield return Catalog.Contracts;
    }
}
```

- [ ] **Step 4: Create `CleanArchitectureLayerTests.cs`**

File: `tests/Kartova.ArchitectureTests/CleanArchitectureLayerTests.cs`

```csharp
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Kartova.ArchitectureTests;

public class CleanArchitectureLayerTests
{
    [Fact]
    public void Domain_Does_Not_Reference_Infrastructure_Or_External_Libraries()
    {
        var result = Types.InAssembly(AssemblyRegistry.Catalog.Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.AspNetCore",
                "Wolverine",
                "Kartova.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer must not reference Infrastructure or external frameworks (ADR-0028). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_Does_Not_Reference_Infrastructure()
    {
        var result = Types.InAssembly(AssemblyRegistry.Catalog.Application)
            .Should()
            .NotHaveDependencyOn("Kartova.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Application layer may depend on Domain and Contracts, never Infrastructure (ADR-0028). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
```

- [ ] **Step 5: Create `ModuleBoundaryTests.cs`**

File: `tests/Kartova.ArchitectureTests/ModuleBoundaryTests.cs`

```csharp
using System.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Kartova.ArchitectureTests;

public class ModuleBoundaryTests
{
    [Fact]
    public void Catalog_Does_Not_Reference_Other_Modules_Internals()
    {
        // In Slice 1 only Catalog exists; this test is vacuously true but scaffolds
        // the rule. Slice 2 adds Organization — extend the forbidden list then.
        var forbiddenNamespaces = new[]
        {
            // placeholder — populated when other modules land
        };

        if (!forbiddenNamespaces.Any())
        {
            // Nothing to enforce yet; register the rule as passing.
            return;
        }

        var catalogAssemblies = new[]
        {
            AssemblyRegistry.Catalog.Domain,
            AssemblyRegistry.Catalog.Application,
            AssemblyRegistry.Catalog.Infrastructure,
            AssemblyRegistry.Catalog.Contracts,
        };

        foreach (var assembly in catalogAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(forbiddenNamespaces)
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Catalog assembly {assembly.GetName().Name} must not reference other modules' internals (ADR-0082). " +
                $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void SharedKernel_Does_Not_Reference_Any_Module()
    {
        var result = Types.InAssembly(AssemblyRegistry.SharedKernel)
            .Should()
            .NotHaveDependencyOnAny(
                "Kartova.Catalog.Domain",
                "Kartova.Catalog.Application",
                "Kartova.Catalog.Infrastructure",
                "Kartova.Catalog.Contracts")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "SharedKernel must be stable and not depend on any module (ADR-0082). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
```

- [ ] **Step 6: Create `ForbiddenDependencyTests.cs`**

File: `tests/Kartova.ArchitectureTests/ForbiddenDependencyTests.cs`

```csharp
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Kartova.ArchitectureTests;

public class ForbiddenDependencyTests
{
    [Fact]
    public void No_Module_References_MediatR()
    {
        foreach (var assembly in AssemblyRegistry.AllProduction())
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("MediatR")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"MediatR is not used per ADR-0080; assembly {assembly.GetName().Name} should route through Wolverine IMessageBus. " +
                $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void No_Module_References_MassTransit()
    {
        foreach (var assembly in AssemblyRegistry.AllProduction())
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("MassTransit")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"MassTransit is not used per ADR-0003/ADR-0080; Kafka is Wolverine (outbound) + KafkaFlow (inbound). " +
                $"Violating types in {assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
```

- [ ] **Step 7: Build and run tests**

```bash
cmd /c dotnet build tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj
cmd /c dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --configuration Release --verbosity normal
```

Expected: 5 tests passed (2 layer + 2 boundary + 2 forbidden = 5... actually: 2 + 2 + 2 = 6. Recount: CleanArchitectureLayerTests has 2, ModuleBoundaryTests has 2, ForbiddenDependencyTests has 2 → **6 tests pass**).

- [ ] **Step 8: Commit**

```bash
git add tests/Kartova.ArchitectureTests/ Kartova.slnx
git commit -m "test(arch): Add NetArchTest rules for Clean Architecture layers, module boundaries, forbidden deps"
```

---

## Task 11: `Kartova.Catalog.Tests` smoke test

**Goal:** Empty xUnit project co-located with Catalog module, one smoke test verifying assembly loads. Real unit tests arrive in Slice 3.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj`
- Create: `src/Modules/Catalog/Kartova.Catalog.Tests/CatalogAssemblyLoadsTests.cs`

- [ ] **Step 1: Create xUnit project**

```bash
cmd /c dotnet new xunit --name Kartova.Catalog.Tests --output src/Modules/Catalog/Kartova.Catalog.Tests --framework net10.0
cmd /c dotnet sln Kartova.slnx add src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj
rm src/Modules/Catalog/Kartova.Catalog.Tests/UnitTest1.cs
```

- [ ] **Step 2: Add references**

```bash
cd src/Modules/Catalog/Kartova.Catalog.Tests
cmd /c dotnet add package FluentAssertions --version 6.12.0
cmd /c dotnet add reference ../Kartova.Catalog.Domain/Kartova.Catalog.Domain.csproj
cmd /c dotnet add reference ../Kartova.Catalog.Application/Kartova.Catalog.Application.csproj
cmd /c dotnet add reference ../Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj
cmd /c dotnet add reference ../Kartova.Catalog.Contracts/Kartova.Catalog.Contracts.csproj
cd ../../../..
```

- [ ] **Step 3: Create smoke test**

File: `src/Modules/Catalog/Kartova.Catalog.Tests/CatalogAssemblyLoadsTests.cs`

```csharp
using FluentAssertions;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Xunit;

namespace Kartova.Catalog.Tests;

public class CatalogAssemblyLoadsTests
{
    [Fact]
    public void All_Catalog_Marker_Types_Resolve()
    {
        typeof(CatalogDomainMarker).Should().NotBeNull();
        typeof(CatalogApplicationMarker).Should().NotBeNull();
        typeof(CatalogInfrastructureAnchor).Should().NotBeNull();
        typeof(CatalogContractsMarker).Should().NotBeNull();
    }

    [Fact]
    public void CatalogModule_Is_Instantiable()
    {
        var sut = new CatalogModule();
        sut.Name.Should().Be("catalog");
    }
}
```

- [ ] **Step 4: Create `CatalogInfrastructureAnchor`**

The test references `CatalogInfrastructureAnchor` which doesn't exist. Create it.

File: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogInfrastructureAnchor.cs`

```csharp
namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Assembly anchor for NetArchTest and test discovery.
/// </summary>
public static class CatalogInfrastructureAnchor { }
```

- [ ] **Step 5: Build and test**

```bash
cmd /c dotnet build src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj
cmd /c dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --configuration Release
```

Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Tests/ src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogInfrastructureAnchor.cs Kartova.slnx
git commit -m "test(catalog): Add Catalog.Tests smoke project with assembly-load and module-name assertions"
```

---

## Task 12: `Kartova.Catalog.IntegrationTests` with Testcontainers PostgreSQL

**Goal:** Integration test that spins up a real PostgreSQL in a container, runs the Catalog migration, asserts `__kartova_metadata` exists and has the seed row. Validates ADR-0085 migrator pipeline end-to-end.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj`
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Fixtures/PostgresFixture.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Migrations/MigrationIntegrationTests.cs`

- [ ] **Step 1: Create xUnit project**

```bash
cmd /c dotnet new xunit --name Kartova.Catalog.IntegrationTests --output src/Modules/Catalog/Kartova.Catalog.IntegrationTests --framework net10.0
cmd /c dotnet sln Kartova.slnx add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj
rm src/Modules/Catalog/Kartova.Catalog.IntegrationTests/UnitTest1.cs
```

- [ ] **Step 2: Add references**

```bash
cd src/Modules/Catalog/Kartova.Catalog.IntegrationTests
cmd /c dotnet add package Testcontainers.PostgreSql --version 3.10.0
cmd /c dotnet add package FluentAssertions --version 6.12.0
cmd /c dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
cmd /c dotnet add package Microsoft.Extensions.Logging --version 10.0.0
cmd /c dotnet add reference ../Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj
cd ../../../..
```

- [ ] **Step 3: Create `PostgresFixture`**

File: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Fixtures/PostgresFixture.cs`

```csharp
using Testcontainers.PostgreSql;
using Xunit;

namespace Kartova.Catalog.IntegrationTests.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Container not started");

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("kartova")
            .WithUsername("migrator")
            .WithPassword("dev")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
```

- [ ] **Step 4: Create `MigrationIntegrationTests`**

File: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Migrations/MigrationIntegrationTests.cs`

```csharp
using FluentAssertions;
using Kartova.Catalog.Infrastructure;
using Kartova.Catalog.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kartova.Catalog.IntegrationTests.Migrations;

public class MigrationIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public MigrationIntegrationTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Initial_Migration_Creates_Metadata_Table_With_Catalog_Row()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;

        await using var ctx = new CatalogDbContext(options);

        // Act
        await ctx.Database.MigrateAsync();

        // Assert — query raw to prove the table name and columns are literal.
        await using var conn = new NpgsqlConnection(_postgres.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT module_name, schema_version FROM __kartova_metadata", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new List<(string ModuleName, int Version)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        rows.Should().ContainSingle()
            .Which.Should().Be(("catalog", 1));
    }

    [Fact]
    public async Task Migration_Is_Idempotent_On_Second_Run()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;

        // First run (may or may not already be applied by previous test — shared fixture).
        await using (var ctx1 = new CatalogDbContext(options))
        {
            await ctx1.Database.MigrateAsync();
        }

        // Second run — must be a no-op, no exception.
        await using var ctx2 = new CatalogDbContext(options);
        var act = async () => await ctx2.Database.MigrateAsync();

        await act.Should().NotThrowAsync();

        // Row should still be unique (ON CONFLICT DO NOTHING).
        await using var conn = new NpgsqlConnection(_postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM __kartova_metadata WHERE module_name = 'catalog'", conn);
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        count.Should().Be(1L);
    }
}
```

- [ ] **Step 5: Build and run**

```bash
cmd /c dotnet build src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj
cmd /c dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --configuration Release
```

Expected: 2 tests pass. First run may take 10-30s while the postgres image is pulled.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ Kartova.slnx
git commit -m "test(catalog): Add MigrationIntegrationTests with Testcontainers PostgreSQL (table creation + idempotency)"
```

---

## Task 13: Dockerfiles for API and migrator

**Goal:** Multi-stage Dockerfiles producing small, non-root, alpine-based images for both the API and the migrator.

**Files:**
- Create: `src/Kartova.Api/Dockerfile`
- Create: `src/Kartova.Migrator/Dockerfile`
- Create: `.dockerignore` (root)

- [ ] **Step 1: Create root `.dockerignore`**

File: `.dockerignore`

```
**/bin
**/obj
**/.vs
**/.idea
**/TestResults
**/node_modules
**/dist
**/.env*
**/.git
**/.github
**/docs
**/*.md
!README.md
!CLAUDE.md
```

- [ ] **Step 2: Create API Dockerfile**

File: `src/Kartova.Api/Dockerfile`

```dockerfile
# syntax=docker/dockerfile:1.7

# ─── build ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG GIT_COMMIT=unknown
ARG BUILD_TIME
WORKDIR /src

COPY global.json ./
COPY Kartova.slnx ./
COPY src/Kartova.SharedKernel/*.csproj src/Kartova.SharedKernel/
COPY src/Kartova.Api/*.csproj src/Kartova.Api/
COPY src/Kartova.Migrator/*.csproj src/Kartova.Migrator/
COPY src/Modules/Catalog/Kartova.Catalog.Domain/*.csproj src/Modules/Catalog/Kartova.Catalog.Domain/
COPY src/Modules/Catalog/Kartova.Catalog.Application/*.csproj src/Modules/Catalog/Kartova.Catalog.Application/
COPY src/Modules/Catalog/Kartova.Catalog.Infrastructure/*.csproj src/Modules/Catalog/Kartova.Catalog.Infrastructure/
COPY src/Modules/Catalog/Kartova.Catalog.Contracts/*.csproj src/Modules/Catalog/Kartova.Catalog.Contracts/

RUN dotnet restore src/Kartova.Api/Kartova.Api.csproj

COPY src/ src/

RUN dotnet publish src/Kartova.Api/Kartova.Api.csproj \
    -c Release \
    -o /out \
    --no-restore \
    /p:InformationalVersion="0.1.0+${GIT_COMMIT}" \
    /p:BuildTime="${BUILD_TIME}"

# ─── runtime ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN addgroup -g 1000 kartova && adduser -D -u 1000 -G kartova kartova
USER kartova:kartova

COPY --from=build --chown=kartova:kartova /out ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
ENTRYPOINT ["dotnet", "Kartova.Api.dll"]
```

- [ ] **Step 3: Create migrator Dockerfile**

File: `src/Kartova.Migrator/Dockerfile`

```dockerfile
# syntax=docker/dockerfile:1.7

# ─── build ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY global.json ./
COPY Kartova.slnx ./
COPY src/Kartova.SharedKernel/*.csproj src/Kartova.SharedKernel/
COPY src/Kartova.Migrator/*.csproj src/Kartova.Migrator/
COPY src/Modules/Catalog/Kartova.Catalog.Domain/*.csproj src/Modules/Catalog/Kartova.Catalog.Domain/
COPY src/Modules/Catalog/Kartova.Catalog.Application/*.csproj src/Modules/Catalog/Kartova.Catalog.Application/
COPY src/Modules/Catalog/Kartova.Catalog.Infrastructure/*.csproj src/Modules/Catalog/Kartova.Catalog.Infrastructure/
COPY src/Modules/Catalog/Kartova.Catalog.Contracts/*.csproj src/Modules/Catalog/Kartova.Catalog.Contracts/

RUN dotnet restore src/Kartova.Migrator/Kartova.Migrator.csproj

COPY src/ src/

RUN dotnet publish src/Kartova.Migrator/Kartova.Migrator.csproj \
    -c Release \
    -o /out \
    --no-restore

# ─── runtime ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN addgroup -g 1000 kartova && adduser -D -u 1000 -G kartova kartova
USER kartova:kartova

COPY --from=build --chown=kartova:kartova /out ./

ENV DOTNET_RUNNING_IN_CONTAINER=true
ENTRYPOINT ["dotnet", "Kartova.Migrator.dll"]
```

- [ ] **Step 4: Build both images locally**

```bash
docker build -t kartova/api:dev -f src/Kartova.Api/Dockerfile .
docker build -t kartova/migrator:dev -f src/Kartova.Migrator/Dockerfile .
```

Expected: both builds succeed. API image ~130-160 MB, migrator ~120-140 MB.

- [ ] **Step 5: Commit**

```bash
git add .dockerignore src/Kartova.Api/Dockerfile src/Kartova.Migrator/Dockerfile
git commit -m "feat(docker): Add multi-stage Dockerfiles for API and migrator (alpine, non-root)"
```

---

## Task 14: `docker-compose.yml` with postgres + migrator + api

**Goal:** Local dev stack that starts PostgreSQL, runs the migrator once, then starts the API only after the migrator succeeds.

**Files:**
- Create: `docker-compose.yml`
- Create: `docker/postgres/init.sql`

- [ ] **Step 1: Create `docker/postgres/init.sql`**

File: `docker/postgres/init.sql`

```sql
-- Roles created at cluster init time (before database creation).
-- Used by local docker-compose only. Production creates these via Helm Secrets + an init Job.

CREATE ROLE migrator WITH LOGIN PASSWORD 'dev' CREATEDB;
CREATE ROLE kartova_app WITH LOGIN PASSWORD 'dev';

-- Grant DML-only role connect rights to the default DB. The migrator role owns the schema.
GRANT CONNECT ON DATABASE kartova TO kartova_app;
```

- [ ] **Step 2: Create `docker-compose.yml`**

File: `docker-compose.yml`

```yaml
services:
  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: kartova
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - ./docker/postgres/init.sql:/docker-entrypoint-initdb.d/01-init.sql:ro
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d kartova"]
      interval: 5s
      timeout: 3s
      retries: 20
      start_period: 5s

  migrator:
    build:
      context: .
      dockerfile: src/Kartova.Migrator/Dockerfile
    image: kartova/migrator:dev
    environment:
      ConnectionStrings__Kartova: "Host=postgres;Port=5432;Database=kartova;Username=migrator;Password=dev"
      DOTNET_ENVIRONMENT: Development
    depends_on:
      postgres:
        condition: service_healthy
    restart: "no"

  api:
    build:
      context: .
      dockerfile: src/Kartova.Api/Dockerfile
      args:
        GIT_COMMIT: "${GIT_COMMIT:-dev}"
        BUILD_TIME: "${BUILD_TIME:-unknown}"
    image: kartova/api:dev
    environment:
      ConnectionStrings__Kartova: "Host=postgres;Port=5432;Database=kartova;Username=kartova_app;Password=dev"
      ASPNETCORE_ENVIRONMENT: Development
      GIT_COMMIT: "${GIT_COMMIT:-dev}"
      BUILD_TIME: "${BUILD_TIME:-unknown}"
    ports:
      - "8080:8080"
    depends_on:
      postgres:
        condition: service_healthy
      migrator:
        condition: service_completed_successfully
    restart: unless-stopped

volumes:
  postgres-data:
```

- [ ] **Step 3: Verify stack starts**

```bash
make up
```

Expected sequence:
1. `postgres` healthcheck passes
2. `migrator` runs, logs "All migrations applied. Exiting.", exits 0
3. `api` starts, listens on :8080

- [ ] **Step 4: Verify endpoints respond**

```bash
curl -s http://localhost:8080/health/live
curl -s http://localhost:8080/health/ready
curl -s http://localhost:8080/health/startup
curl -s http://localhost:8080/api/v1/version
```

Expected outputs: all four return 200, each with the expected body per spec Success Criteria.

- [ ] **Step 5: Verify metadata row**

```bash
docker compose exec postgres psql -U postgres -d kartova -c "SELECT * FROM __kartova_metadata"
```

Expected: one row `catalog | 1 | <timestamp>`.

- [ ] **Step 6: Clean up**

```bash
make down
```

- [ ] **Step 7: Commit**

```bash
git add docker-compose.yml docker/postgres/init.sql
git commit -m "feat(docker): Add docker-compose with postgres + migrator + api and init.sql for roles"
```

---

## Task 15: Frontend scaffold (Vite + React + TS strict + Tailwind v4)

**Goal:** Initialize `web/` with Vite, TypeScript strict, Tailwind v4.

**Files:**
- Create: `web/package.json`
- Create: `web/tsconfig.json`
- Create: `web/tsconfig.app.json`
- Create: `web/tsconfig.node.json`
- Create: `web/vite.config.ts`
- Create: `web/index.html`
- Create: `web/src/main.tsx`
- Create: `web/src/index.css`
- Create: `web/src/App.tsx` (placeholder — full layout in Task 17)
- Create: `web/src/vite-env.d.ts`
- Create: `web/postcss.config.js`
- Create: `web/tailwind.config.ts`

- [ ] **Step 1: Initialize Vite project (Node 20+)**

```bash
cmd /c npm create vite@latest web -- --template react-ts
cd web
cmd /c npm install
cd ..
```

Vite scaffolds `package.json`, `tsconfig.json`, `vite.config.ts`, `index.html`, `src/{main.tsx,App.tsx,index.css}`.

- [ ] **Step 2: Upgrade `package.json` to pinned versions and add Tailwind v4**

File: `web/package.json`

```json
{
  "name": "kartova-web",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "typecheck": "tsc -b --noEmit",
    "lint": "eslint .",
    "preview": "vite preview"
  },
  "dependencies": {
    "react": "^19.0.0",
    "react-dom": "^19.0.0",
    "react-router-dom": "^7.1.0",
    "lucide-react": "^0.469.0",
    "clsx": "^2.1.1",
    "tailwind-merge": "^2.5.5"
  },
  "devDependencies": {
    "@types/react": "^19.0.0",
    "@types/react-dom": "^19.0.0",
    "@vitejs/plugin-react": "^4.3.4",
    "tailwindcss": "^4.0.0",
    "@tailwindcss/vite": "^4.0.0",
    "typescript": "^5.7.0",
    "vite": "^6.0.0"
  }
}
```

- [ ] **Step 3: Install dependencies**

```bash
cd web
cmd /c npm install
cd ..
```

- [ ] **Step 4: Update `tsconfig.json` (strict)**

File: `web/tsconfig.json`

```json
{
  "files": [],
  "references": [
    { "path": "./tsconfig.app.json" },
    { "path": "./tsconfig.node.json" }
  ]
}
```

File: `web/tsconfig.app.json`

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "useDefineForClassFields": true,
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "isolatedModules": true,
    "moduleDetection": "force",
    "noEmit": true,
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "noUncheckedIndexedAccess": true,
    "baseUrl": ".",
    "paths": {
      "@/*": ["./src/*"]
    }
  },
  "include": ["src"]
}
```

File: `web/tsconfig.node.json`

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2023"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "isolatedModules": true,
    "moduleDetection": "force",
    "noEmit": true,
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true
  },
  "include": ["vite.config.ts"]
}
```

- [ ] **Step 5: Update `vite.config.ts`**

File: `web/vite.config.ts`

```typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "node:path";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5173,
  },
});
```

- [ ] **Step 6: Create `index.html`**

File: `web/index.html`

```html
<!doctype html>
<html lang="en" class="dark">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="/vite.svg" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Kartova</title>
  </head>
  <body class="bg-background text-foreground antialiased">
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 7: Create `src/index.css` (Tailwind v4 import + DESIGN.md tokens)**

File: `web/src/index.css`

```css
@import "tailwindcss";

@theme {
  /* Slate palette — mirrors docs/design/DESIGN.md */
  --color-background: hsl(222 47% 8%);   /* Slate 900 */
  --color-card: hsl(222 47% 11%);         /* Slate 800 */
  --color-card-elevated: hsl(222 47% 14%);/* Slate 750 */
  --color-border: hsl(222 47% 18%);       /* Slate 700 */
  --color-foreground: hsl(210 40% 96%);   /* Slate 100 */
  --color-muted: hsl(215 16% 57%);        /* Slate 400 */
  --color-primary: hsl(217 91% 60%);      /* Blue 500 */
  --color-primary-foreground: hsl(0 0% 100%);
  --color-success: hsl(142 71% 45%);
  --color-warning: hsl(38 92% 50%);
  --color-danger: hsl(0 84% 60%);

  /* Typography */
  --font-sans: "Inter", system-ui, sans-serif;
  --font-mono: "JetBrains Mono", ui-monospace, monospace;

  /* Radii */
  --radius: 0.5rem;
}

html, body, #root {
  height: 100%;
  margin: 0;
}

body {
  font-family: var(--font-sans);
}
```

- [ ] **Step 8: Create `src/main.tsx`**

File: `web/src/main.tsx`

```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import "./index.css";
import App from "./App";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
```

- [ ] **Step 9: Placeholder `App.tsx` (full layout in Task 17)**

File: `web/src/App.tsx`

```tsx
export default function App() {
  return (
    <div className="flex h-full items-center justify-center">
      <p className="text-muted">Kartova — bootstrapping...</p>
    </div>
  );
}
```

- [ ] **Step 10: Build and verify**

```bash
cd web
cmd /c npm run typecheck
cmd /c npm run build
cd ..
```

Expected: both commands succeed, `web/dist/` produced.

- [ ] **Step 11: Commit**

```bash
git add web/
git commit -m "feat(web): Scaffold Vite + React 19 + TS strict + Tailwind v4 with DESIGN.md tokens"
```

---

## Task 16: shadcn/ui init + install Button, Card, Sidebar components

**Goal:** shadcn/ui configured to copy component source files into `web/src/components/ui/`. Install three base components.

**Files:**
- Create: `web/components.json`
- Create: `web/src/lib/utils.ts`
- Create: `web/src/components/ui/button.tsx` (via shadcn CLI)
- Create: `web/src/components/ui/card.tsx` (via shadcn CLI)
- Create: `web/src/components/ui/sidebar.tsx` (via shadcn CLI)

- [ ] **Step 1: Create `lib/utils.ts`**

File: `web/src/lib/utils.ts`

```typescript
import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
```

- [ ] **Step 2: Create `components.json` for shadcn**

File: `web/components.json`

```json
{
  "$schema": "https://ui.shadcn.com/schema.json",
  "style": "new-york",
  "rsc": false,
  "tsx": true,
  "tailwind": {
    "config": "",
    "css": "src/index.css",
    "baseColor": "slate",
    "cssVariables": true
  },
  "aliases": {
    "components": "@/components",
    "utils": "@/lib/utils",
    "ui": "@/components/ui",
    "lib": "@/lib",
    "hooks": "@/hooks"
  },
  "iconLibrary": "lucide"
}
```

- [ ] **Step 3: Install three base components via shadcn CLI**

```bash
cd web
cmd /c npx shadcn@latest add button card sidebar --yes
cd ..
```

Expected: CLI creates `src/components/ui/button.tsx`, `card.tsx`, `sidebar.tsx`. Adds any required package deps (e.g., `@radix-ui/react-slot`).

- [ ] **Step 4: Run typecheck**

```bash
cd web
cmd /c npm run typecheck
cd ..
```

Expected: passes. If it fails due to a missing peer dep shadcn CLI didn't auto-install, add it via `npm install <package>` and re-run.

- [ ] **Step 5: Commit**

```bash
git add web/
git commit -m "feat(web): Init shadcn/ui and install Button, Card, Sidebar base components"
```

---

## Task 17: Frontend layout — Sidebar + TopBar + routing

**Goal:** Implement `Sidebar` and `TopBar` layout components per DESIGN.md nav spec (canonical, NOT from Stitch). Set up routing: `/` redirects to `/catalog`, `/catalog` renders placeholder.

**Files:**
- Create: `web/src/components/layout/Sidebar.tsx`
- Create: `web/src/components/layout/TopBar.tsx`
- Create: `web/src/components/layout/AppLayout.tsx`
- Create: `web/src/pages/CatalogPlaceholder.tsx`
- Modify: `web/src/App.tsx`

- [ ] **Step 1: Create `Sidebar.tsx`** (per DESIGN.md: 260px width, dark Slate bg, nav items with `Catalog` active)

File: `web/src/components/layout/Sidebar.tsx`

```tsx
import { NavLink } from "react-router-dom";
import { Folder, Server, Book, Settings as SettingsIcon, Boxes } from "lucide-react";
import { cn } from "@/lib/utils";

interface NavItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  enabled: boolean;
}

const NAV_ITEMS: NavItem[] = [
  { to: "/catalog", label: "Catalog", icon: Folder, enabled: true },
  { to: "/services", label: "Services", icon: Boxes, enabled: false },
  { to: "/infrastructure", label: "Infrastructure", icon: Server, enabled: false },
  { to: "/docs", label: "Docs", icon: Book, enabled: false },
  { to: "/settings", label: "Settings", icon: SettingsIcon, enabled: false },
];

export function Sidebar() {
  return (
    <aside className="flex h-full w-[260px] flex-col border-r border-border bg-card">
      <div className="flex h-14 items-center border-b border-border px-4">
        <span className="text-lg font-semibold">Kartova</span>
      </div>
      <nav className="flex-1 overflow-y-auto p-3">
        <ul className="space-y-1">
          {NAV_ITEMS.map(item => (
            <li key={item.to}>
              {item.enabled ? (
                <NavLink
                  to={item.to}
                  className={({ isActive }) =>
                    cn(
                      "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
                      isActive
                        ? "bg-primary text-primary-foreground"
                        : "text-foreground hover:bg-card-elevated",
                    )
                  }
                >
                  <item.icon className="h-4 w-4" />
                  {item.label}
                </NavLink>
              ) : (
                <span
                  className="flex cursor-not-allowed items-center gap-3 rounded-md px-3 py-2 text-sm text-muted opacity-50"
                  data-disabled="true"
                >
                  <item.icon className="h-4 w-4" />
                  {item.label}
                </span>
              )}
            </li>
          ))}
        </ul>
      </nav>
    </aside>
  );
}
```

- [ ] **Step 2: Create `TopBar.tsx`** (56px height per DESIGN.md, logo + disabled search)

File: `web/src/components/layout/TopBar.tsx`

```tsx
import { Search } from "lucide-react";

export function TopBar() {
  return (
    <header className="flex h-14 items-center border-b border-border bg-card px-6">
      <div className="flex flex-1 items-center gap-4">
        <div className="relative w-full max-w-xl">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
          <input
            type="text"
            placeholder="Search entities..."
            disabled
            className="w-full rounded-md border border-border bg-background py-2 pl-9 pr-3 text-sm text-muted placeholder:text-muted disabled:cursor-not-allowed"
          />
        </div>
      </div>
    </header>
  );
}
```

- [ ] **Step 3: Create `AppLayout.tsx`**

File: `web/src/components/layout/AppLayout.tsx`

```tsx
import { Outlet } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { TopBar } from "./TopBar";

export function AppLayout() {
  return (
    <div className="flex h-full">
      <Sidebar />
      <div className="flex flex-1 flex-col overflow-hidden">
        <TopBar />
        <main className="flex-1 overflow-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Create placeholder catalog page**

File: `web/src/pages/CatalogPlaceholder.tsx`

```tsx
import { Card } from "@/components/ui/card";

export function CatalogPlaceholder() {
  return (
    <Card className="flex h-full items-center justify-center p-8">
      <div className="text-center">
        <h1 className="text-2xl font-semibold">Catalog</h1>
        <p className="mt-2 text-muted">Coming in Slice 3 — entity registration &amp; browsing.</p>
      </div>
    </Card>
  );
}
```

- [ ] **Step 5: Wire routes in `App.tsx`**

File: `web/src/App.tsx`

```tsx
import { Navigate, Route, Routes } from "react-router-dom";
import { AppLayout } from "@/components/layout/AppLayout";
import { CatalogPlaceholder } from "@/pages/CatalogPlaceholder";

export default function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route path="/" element={<Navigate to="/catalog" replace />} />
        <Route path="/catalog" element={<CatalogPlaceholder />} />
      </Route>
    </Routes>
  );
}
```

- [ ] **Step 6: Run dev server and verify**

```bash
cd web
cmd /c npm run dev
```

In a browser (or via Playwright MCP) visit `http://localhost:5173`:
- Observe redirect `/` → `/catalog`
- Sidebar shows `Catalog` active, four items disabled with `data-disabled`
- TopBar renders with disabled search
- Placeholder card visible in main content area
- Open DevTools console → zero errors

- [ ] **Step 7: Stop dev server and commit**

Ctrl+C to stop. Then:

```bash
git add web/
git commit -m "feat(web): Add AppLayout with Sidebar + TopBar per DESIGN.md, /catalog placeholder route"
```

---

## Task 18: Frontend Dockerfile (nginx multi-stage)

**Goal:** Production-shaped frontend image serving static Vite build via nginx.

**Files:**
- Create: `web/Dockerfile`
- Create: `web/nginx.conf`
- Create: `web/.dockerignore`

- [ ] **Step 1: Create `web/.dockerignore`**

File: `web/.dockerignore`

```
node_modules
dist
.vite
.env*
.git
```

- [ ] **Step 2: Create `web/nginx.conf`**

File: `web/nginx.conf`

```nginx
server {
  listen 80;
  root /usr/share/nginx/html;
  index index.html;

  location / {
    try_files $uri /index.html;
  }

  location ~* \.(js|css|woff2?|svg|png|jpg|ico)$ {
    expires 1y;
    add_header Cache-Control "public, immutable";
  }
}
```

- [ ] **Step 3: Create `web/Dockerfile`**

File: `web/Dockerfile`

```dockerfile
# syntax=docker/dockerfile:1.7

# ─── build ───────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts

COPY . .
RUN npm run build

# ─── runtime ─────────────────────────────────────────────────────────────
FROM nginx:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
```

- [ ] **Step 4: Build locally to verify**

```bash
docker build -t kartova/web:dev -f web/Dockerfile web/
```

Expected: image built, ~25-40 MB.

- [ ] **Step 5: Commit**

```bash
git add web/Dockerfile web/nginx.conf web/.dockerignore
git commit -m "feat(web): Add multi-stage Dockerfile (node build + nginx serve)"
```

---

## Task 19: Helm chart skeleton

**Goal:** Minimal `deploy/helm/kartova/` chart that passes `helm lint` and `helm template | kubectl apply --dry-run=client`. Full production templates come in Slice 4.

**Files:**
- Create: `deploy/helm/kartova/Chart.yaml`
- Create: `deploy/helm/kartova/values.yaml`
- Create: `deploy/helm/kartova/templates/_helpers.tpl`
- Create: `deploy/helm/kartova/templates/api-deployment.yaml`
- Create: `deploy/helm/kartova/templates/migrator-job.yaml`
- Create: `deploy/helm/kartova/.helmignore`

- [ ] **Step 1: Create `Chart.yaml`**

File: `deploy/helm/kartova/Chart.yaml`

```yaml
apiVersion: v2
name: kartova
description: Kartova service catalog and developer portal
type: application
version: 0.1.0
appVersion: "0.1.0"
keywords:
  - service-catalog
  - developer-portal
  - backstage-alternative
maintainers:
  - name: Roman Głogowski
    email: 18666546+diablo39@users.noreply.github.com
```

- [ ] **Step 2: Create `values.yaml`**

File: `deploy/helm/kartova/values.yaml`

```yaml
nameOverride: ""
fullnameOverride: ""

image:
  repository: ghcr.io/romanglogowski/kartova
  tag: "0.1.0"
  pullPolicy: IfNotPresent

api:
  replicaCount: 1
  image:
    name: api
  port: 8080
  resources:
    requests:
      cpu: 100m
      memory: 256Mi
    limits:
      cpu: 500m
      memory: 512Mi

migrator:
  image:
    name: migrator
  resources:
    requests:
      cpu: 100m
      memory: 128Mi
    limits:
      cpu: 500m
      memory: 256Mi

database:
  connectionString: ""   # set via values override or external secret

env:
  ASPNETCORE_ENVIRONMENT: Production
```

- [ ] **Step 3: Create `.helmignore`**

File: `deploy/helm/kartova/.helmignore`

```
.DS_Store
.git/
.vscode/
*.swp
*.tmp
*.orig
README.md
```

- [ ] **Step 4: Create `_helpers.tpl`**

File: `deploy/helm/kartova/templates/_helpers.tpl`

```yaml
{{/*
Expand the name of the chart.
*/}}
{{- define "kartova.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "kartova.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Chart label block.
*/}}
{{- define "kartova.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{ include "kartova.selectorLabels" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "kartova.selectorLabels" -}}
app.kubernetes.io/name: {{ include "kartova.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}
```

- [ ] **Step 5: Create `api-deployment.yaml`**

File: `deploy/helm/kartova/templates/api-deployment.yaml`

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "kartova.fullname" . }}-api
  labels:
    {{- include "kartova.labels" . | nindent 4 }}
    app.kubernetes.io/component: api
spec:
  replicas: {{ .Values.api.replicaCount }}
  selector:
    matchLabels:
      {{- include "kartova.selectorLabels" . | nindent 6 }}
      app.kubernetes.io/component: api
  template:
    metadata:
      labels:
        {{- include "kartova.selectorLabels" . | nindent 8 }}
        app.kubernetes.io/component: api
    spec:
      containers:
        - name: api
          image: "{{ .Values.image.repository }}-{{ .Values.api.image.name }}:{{ .Values.image.tag | default .Chart.AppVersion }}"
          imagePullPolicy: {{ .Values.image.pullPolicy }}
          ports:
            - name: http
              containerPort: {{ .Values.api.port }}
              protocol: TCP
          env:
            - name: ConnectionStrings__Kartova
              value: {{ .Values.database.connectionString | quote }}
            - name: ASPNETCORE_ENVIRONMENT
              value: {{ .Values.env.ASPNETCORE_ENVIRONMENT | quote }}
          livenessProbe:
            httpGet:
              path: /health/live
              port: http
            initialDelaySeconds: 10
            periodSeconds: 10
          readinessProbe:
            httpGet:
              path: /health/ready
              port: http
            initialDelaySeconds: 5
            periodSeconds: 5
          startupProbe:
            httpGet:
              path: /health/startup
              port: http
            failureThreshold: 30
            periodSeconds: 5
          resources:
            {{- toYaml .Values.api.resources | nindent 12 }}
```

- [ ] **Step 6: Create `migrator-job.yaml`**

File: `deploy/helm/kartova/templates/migrator-job.yaml`

```yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: {{ include "kartova.fullname" . }}-migrator-{{ .Release.Revision }}
  labels:
    {{- include "kartova.labels" . | nindent 4 }}
    app.kubernetes.io/component: migrator
  annotations:
    "helm.sh/hook": pre-install,pre-upgrade
    "helm.sh/hook-weight": "-10"
    "helm.sh/hook-delete-policy": before-hook-creation,hook-succeeded
spec:
  backoffLimit: 1
  template:
    metadata:
      labels:
        {{- include "kartova.selectorLabels" . | nindent 8 }}
        app.kubernetes.io/component: migrator
    spec:
      restartPolicy: Never
      containers:
        - name: migrator
          image: "{{ .Values.image.repository }}-{{ .Values.migrator.image.name }}:{{ .Values.image.tag | default .Chart.AppVersion }}"
          imagePullPolicy: {{ .Values.image.pullPolicy }}
          env:
            - name: ConnectionStrings__Kartova
              value: {{ .Values.database.connectionString | quote }}
          resources:
            {{- toYaml .Values.migrator.resources | nindent 12 }}
```

- [ ] **Step 7: Validate chart**

```bash
helm lint deploy/helm/kartova/ --set database.connectionString="Host=test;Database=test;Username=test;Password=test"
helm template deploy/helm/kartova/ --set database.connectionString="Host=test;Database=test;Username=test;Password=test" > /tmp/kartova-rendered.yaml
cat /tmp/kartova-rendered.yaml | head -30
```

Expected: `helm lint` reports `1 chart(s) linted, 0 chart(s) failed`; template renders YAML with valid structure.

- [ ] **Step 8: Commit**

```bash
git add deploy/helm/kartova/
git commit -m "feat(helm): Add Kartova Helm chart skeleton (Chart.yaml, values.yaml, api Deployment, migrator pre-upgrade Job)"
```

---

## Task 20: GitHub Actions CI workflow

**Goal:** `.github/workflows/ci.yml` running backend (arch + unit + integration) and frontend (typecheck + build) in parallel.

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Create `ci.yml`**

File: `.github/workflows/ci.yml`

```yaml
name: CI

on:
  push:
    branches: [master]
  pull_request:
    branches: [master]

concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

jobs:
  backend:
    name: Backend (arch + unit + integration)
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Restore
        run: dotnet restore Kartova.slnx

      - name: Build
        run: dotnet build Kartova.slnx --configuration Release --no-restore

      - name: Architecture tests (CI gate — ADR-0083)
        run: dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --configuration Release --no-build --verbosity normal

      - name: Unit tests
        run: dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --configuration Release --no-build --verbosity normal

      - name: Integration tests (Testcontainers)
        run: dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --configuration Release --no-build --verbosity normal

  frontend:
    name: Frontend (typecheck + build)
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: web
    steps:
      - uses: actions/checkout@v4

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: "20"
          cache: "npm"
          cache-dependency-path: web/package-lock.json

      - name: Install
        run: npm ci

      - name: Typecheck
        run: npm run typecheck

      - name: Build
        run: npm run build

  helm:
    name: Helm (lint + template)
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup Helm
        uses: azure/setup-helm@v4
        with:
          version: "v3.16.0"

      - name: Lint
        run: helm lint deploy/helm/kartova/ --set database.connectionString="Host=x;Database=x;Username=x;Password=x"

      - name: Template
        run: helm template deploy/helm/kartova/ --set database.connectionString="Host=x;Database=x;Username=x;Password=x" > /tmp/rendered.yaml
```

- [ ] **Step 2: Commit**

```bash
git add .github/
git commit -m "ci: Add GitHub Actions workflow (backend + frontend + helm jobs in parallel)"
```

- [ ] **Step 3: (Optional) Verify by pushing to a WIP branch**

If a remote is configured and this is safe to push:

```bash
git checkout -b slice-1/ci-verification
git push --set-upstream origin slice-1/ci-verification
```

Watch the Actions tab for green runs. Return to master when green:

```bash
git checkout master
git branch -D slice-1/ci-verification  # local cleanup
```

> If CI fails because a package version drifts, update the referenced version in the relevant csproj and re-commit. Do not lower `TreatWarningsAsErrors`.

---

## Task 21: Final verification pass & CHECKLIST.md update

**Goal:** Run every Success Criteria item from the spec and confirm green. Update `docs/product/CHECKLIST.md` for completed E-01.F-01 stories.

**Files:**
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Fresh stack start**

```bash
make down
make up
```

Expected: postgres healthy → migrator runs and exits 0 → api listens on :8080.

- [ ] **Step 2: Backend endpoints**

```bash
curl -s -w "%{http_code}\n" http://localhost:8080/health/live
curl -s -w "%{http_code}\n" http://localhost:8080/health/ready
curl -s -w "%{http_code}\n" http://localhost:8080/health/startup
curl -s http://localhost:8080/api/v1/version | head
```

Expected: all return 200; `/api/v1/version` returns JSON with `version`, `commit`, `buildTime`.

- [ ] **Step 3: Metadata table**

```bash
docker compose exec postgres psql -U postgres -d kartova -c "SELECT * FROM __kartova_metadata"
```

Expected: one row `catalog | 1 | <timestamp>`.

- [ ] **Step 4: Tests**

```bash
make archtest
make test
```

Expected: all green, total wall clock < 30 s.

- [ ] **Step 5: Frontend**

In a second terminal:

```bash
make web
```

Visit `http://localhost:5173`. Expected:
- `/` redirects to `/catalog`
- Sidebar + TopBar render per DESIGN.md (Slate dark palette, `Catalog` active, others disabled)
- Placeholder "Coming in Slice 3" card visible
- DevTools console shows zero errors

Use **Playwright MCP** to automate this check (per ADR-0084):

```
browser_navigate http://localhost:5173
browser_snapshot
browser_console_messages
```

Expected: snapshot shows the layout; console has zero errors.

- [ ] **Step 6: Frontend build**

```bash
cd web
cmd /c npm run typecheck
cmd /c npm run build
cd ..
```

Expected: both pass; `web/dist/` produced.

- [ ] **Step 7: Helm**

```bash
helm lint deploy/helm/kartova/ --set database.connectionString="Host=x;Database=x;Username=x;Password=x"
```

Expected: `0 chart(s) failed`.

- [ ] **Step 8: Clean shutdown**

```bash
make down
```

Expected: all containers stopped and removed, volumes cleaned.

- [ ] **Step 9: Update `docs/product/CHECKLIST.md`**

Open `docs/product/CHECKLIST.md`. Find the Phase 0 → E-01.F-01 section. Tick completed stories. Specifically:

- `E-01.F-01.S-01` Project scaffolding — ✅
- `E-01.F-01.S-02` Module structure convention — ✅
- `E-01.F-01.S-03` Docker Compose local dev baseline — ✅
- (and any other Phase 0 stories this slice touched; use the existing CHECKLIST.md as the authoritative list of which story IDs)

For each completed story, change `- [ ]` to `- [x]`. If a story is only partially done (e.g., E-01.F-02 CI/CD has items not yet covered), leave it unchecked and note in commit message.

- [ ] **Step 10: Commit CHECKLIST.md**

```bash
git add docs/product/CHECKLIST.md
git commit -m "docs: Mark E-01.F-01 stories complete after Slice 1 (walking skeleton)"
```

- [ ] **Step 11: Create release commit message summary**

Optionally create an empty summary commit to mark slice completion:

```bash
git commit --allow-empty -m "chore: Slice 1 walking skeleton complete — all success criteria green"
```

---

## Self-review summary

After writing the plan I verified:

**Spec coverage** — every spec Success Criteria item maps to a task:
- Infrastructure criteria → Tasks 13, 14, 21
- Backend endpoints → Task 8, verified in 21
- Frontend shell → Tasks 15, 16, 17, verified in 21
- Tests → Tasks 10, 11, 12, verified in 21
- CI → Task 20
- Helm → Task 19

**Out-of-scope respected** — no tasks for auth, RLS, KafkaFlow, Wolverine handlers, real entities, full Helm templates, GDPR, audit log, E2E/contract tests, or Elasticsearch/MinIO.

**Placeholder scan** — no "TODO" / "TBD" / "implement later" / "similar to Task N" in any step.

**Type consistency** — `CatalogDomainMarker`, `CatalogApplicationMarker`, `CatalogContractsMarker`, `CatalogInfrastructureAnchor`, `CatalogModule` all referenced consistently across Tasks 4, 5, 6, 10, 11.

**Flagged decisions** — container registry path, dev DB credentials, database name, package version wildcards are called out in the header block for user confirmation before execution.

**Known follow-ups (not Slice 1 scope, recorded for Slice 3+):**
- `IMigrationTarget` abstraction to replace the explicit `GetService<CatalogDbContext>()` in `Kartova.Migrator/Program.cs` once a second module has a DbContext
- Update ADR-0083 Implementation Notes to reflect co-located module tests (spec calls this out)

---

## Execution handoff

**Plan complete and saved to `docs/superpowers/plans/2026-04-21-walking-skeleton-plan.md`.**

**Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration via `superpowers:subagent-driven-development`.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints for review.

**Which approach?**
