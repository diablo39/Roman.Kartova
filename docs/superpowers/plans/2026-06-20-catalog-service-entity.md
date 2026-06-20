# Catalog Service Entity — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `Service` aggregate to the Catalog module — register a service with `0..50` protocol-typed endpoints, read one by id, and list services with the standard cursor-paginated contract.

**Architecture:** `Service` is a sibling aggregate to `Application` in the existing Catalog module (no new csproj). It reuses every Application pattern: tenant scope (ADR-0090), required owning team + membership gate (ADR-0103), direct-dispatch handlers (ADR-0093), cursor pagination (ADR-0095), fail-closed in-transaction audit. The single novel element is an owned collection of endpoint value objects persisted to one `jsonb` column.

**Tech Stack:** .NET 10 / C# · EF Core 10 + Npgsql (jsonb owned collection via `OwnsMany(...).ToJson()`) · PostgreSQL 18 RLS · MSTest v4 + NSubstitute · Testcontainers (real Postgres) + real `JwtBearer`.

## Global Constraints

- Solution file `Kartova.slnx`; build with `TreatWarningsAsErrors=true` (0 warnings).
- Windows shell: use PowerShell or `cmd //c` wrappers for `dotnet` in Git Bash.
- `*Dto`/`*Request`/`*Response` contract types and any `IModule` class MUST carry `[ExcludeFromCodeCoverage]` (enforced by `ContractsCoverageRules`).
- Tenant id + created-by user id come from `ITenantContext`/`ICurrentUser`, never the request payload (ADR-0090). Team id comes from the payload and is validated to exist in the tenant.
- Endpoints: `0` allowed, **`> 50` rejected** by a `Service.Create` invariant.
- `Service` has **no `Lifecycle`** this slice.
- `Health` defaults to `HealthStatus.Unknown` and has **no write path** this slice.
- RLS DDL lives in the EF migration (migrator is the sole schema owner, ADR-0085). Catalog convention = `ENABLE` + `FORCE ROW LEVEL SECURITY` + a `tenant_isolation` policy on the strict `current_setting('app.current_tenant_id')::uuid` form (mirror `AddApplications`; the spec's word "REVOKE" was imprecise — catalog tables use FORCE-RLS, REVOKE is audit-only).
- New permission string is the stable wire contract; reads reuse `catalog.read`.
- Audit action `service.registered` / target type `Service` are stable strings written to `audit_log` — do not rename without a row migration.

---

### Task 1: Value types — `ServiceId`, `Protocol`, `HealthStatus`, `ServiceEndpoint`

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/ServiceId.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/Protocol.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/HealthStatus.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/ServiceEndpoint.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ServiceEndpointTests.cs`

**Interfaces:**
- Produces: `ServiceId(Guid Value)` + `ServiceId.New()`; enums `Protocol { Rest, Grpc, GraphQL, WebSocket, Tcp, Other }` and `HealthStatus { Unknown, Healthy, Degraded, Unhealthy }` (both in `Kartova.Catalog.Domain`, referenced by Contracts the same way `Lifecycle` is); `ServiceEndpoint(string Url, Protocol Protocol)` with get-only `Url`/`Protocol` and a validating constructor.

- [ ] **Step 1: Write the failing test**

`src/Modules/Catalog/Kartova.Catalog.Tests/ServiceEndpointTests.cs`:

```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ServiceEndpointTests
{
    [TestMethod]
    public void Ctor_with_valid_absolute_url_and_protocol_sets_properties()
    {
        var ep = new ServiceEndpoint("https://api.example.com/v1", Protocol.Rest);
        Assert.AreEqual("https://api.example.com/v1", ep.Url);
        Assert.AreEqual(Protocol.Rest, ep.Protocol);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Ctor_throws_on_empty_url(string url) =>
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceEndpoint(url, Protocol.Rest));

    [TestMethod]
    public void Ctor_throws_on_relative_url() =>
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceEndpoint("/v1/orders", Protocol.Rest));

    [TestMethod]
    public void Ctor_throws_on_url_over_2048_chars() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new ServiceEndpoint("https://x/" + new string('a', 2048), Protocol.Rest));

    [TestMethod]
    public void Ctor_throws_on_undefined_protocol() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new ServiceEndpoint("https://api.example.com", (Protocol)999));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from repo root): `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -c Debug"`
Expected: FAIL — `ServiceEndpoint`, `Protocol` do not exist (compile error).

- [ ] **Step 3: Write the value types**

`src/Modules/Catalog/Kartova.Catalog.Domain/ServiceId.cs`:

```csharp
namespace Kartova.Catalog.Domain;

public readonly record struct ServiceId(Guid Value)
{
    public static ServiceId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
```

`src/Modules/Catalog/Kartova.Catalog.Domain/Protocol.cs`:

```csharp
namespace Kartova.Catalog.Domain;

/// <summary>Transport/interface style of a service endpoint. Closed vocabulary;
/// <c>Other</c> is the escape hatch so the enum need not churn (mirrors the
/// fixed-vocabulary stance of ADR-0068).</summary>
public enum Protocol
{
    Rest,
    Grpc,
    GraphQL,
    WebSocket,
    Tcp,
    Other,
}
```

`src/Modules/Catalog/Kartova.Catalog.Domain/HealthStatus.cs`:

```csharp
namespace Kartova.Catalog.Domain;

/// <summary>Operational health of a service. Defaults to <c>Unknown</c>; the
/// write path (probe/agent ingestion) lands in a later phase (E-15/E-16).</summary>
public enum HealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy,
}
```

`src/Modules/Catalog/Kartova.Catalog.Domain/ServiceEndpoint.cs`:

```csharp
namespace Kartova.Catalog.Domain;

/// <summary>
/// Value object: one network endpoint a service exposes, with its protocol.
/// Validated on construction; EF rehydrates it from jsonb via this same
/// constructor (param names match the Url/Protocol properties).
/// </summary>
public sealed record ServiceEndpoint
{
    public string Url { get; }
    public Protocol Protocol { get; }

    public ServiceEndpoint(string url, Protocol protocol)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("endpoint url must not be empty.", nameof(url));
        if (url.Length > 2048)
            throw new ArgumentException("endpoint url must be <= 2048 characters.", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new ArgumentException("endpoint url must be an absolute URI.", nameof(url));
        if (!Enum.IsDefined(protocol))
            throw new ArgumentException("unknown protocol.", nameof(protocol));

        Url = url;
        Protocol = protocol;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -c Debug"`
Expected: PASS (all `ServiceEndpointTests`).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/ServiceId.cs \
        src/Modules/Catalog/Kartova.Catalog.Domain/Protocol.cs \
        src/Modules/Catalog/Kartova.Catalog.Domain/HealthStatus.cs \
        src/Modules/Catalog/Kartova.Catalog.Domain/ServiceEndpoint.cs \
        src/Modules/Catalog/Kartova.Catalog.Tests/ServiceEndpointTests.cs
git commit -m "feat(catalog): add Service value types (ServiceId, Protocol, HealthStatus, ServiceEndpoint)"
```

---

### Task 2: `Service` aggregate + `Create` invariants

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/Service.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ServiceTests.cs`

**Interfaces:**
- Consumes: `ServiceId`, `ServiceEndpoint`, `HealthStatus`, `Protocol` (Task 1); `TenantId`, `ITenantOwned`, `ITeamScopedResource` (`Kartova.SharedKernel.Multitenancy`).
- Produces: `Service.Create(string displayName, string description, Guid createdByUserId, Guid teamId, IEnumerable<ServiceEndpoint> endpoints, TenantId tenantId, TimeProvider clock)` and an explicit-`createdAt` overload; read-only properties `Id (ServiceId)`, `TenantId`, `DisplayName`, `Description`, `TeamId`, `CreatedByUserId`, `CreatedAt`, `Health`, `Endpoints (IReadOnlyList<ServiceEndpoint>)`, `Version (uint)`.

- [ ] **Step 1: Write the failing tests**

`src/Modules/Catalog/Kartova.Catalog.Tests/ServiceTests.cs`:

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ServiceTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Team = Guid.NewGuid();
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-06-20T10:00:00Z"));

    private static ServiceEndpoint Ep(string u = "https://api.example.com/v1") => new(u, Protocol.Rest);

    [TestMethod]
    public void Create_with_valid_args_sets_fields_and_defaults_health_unknown()
    {
        var s = Service.Create("orders-svc", "Order service.", Creator, Team, new[] { Ep() }, Tenant, Clock);

        Assert.AreEqual("orders-svc", s.DisplayName);
        Assert.AreEqual("Order service.", s.Description);
        Assert.AreEqual(Creator, s.CreatedByUserId);
        Assert.AreEqual(Team, s.TeamId);
        Assert.AreEqual(Tenant, s.TenantId);
        Assert.AreEqual(HealthStatus.Unknown, s.Health);
        Assert.AreEqual(1, s.Endpoints.Count);
        Assert.AreEqual(Clock.GetUtcNow(), s.CreatedAt);
        Assert.AreNotEqual(Guid.Empty, s.Id.Value);
    }

    [TestMethod]
    public void Create_allows_zero_endpoints()
    {
        var s = Service.Create("svc", "No endpoints yet.", Creator, Team, Array.Empty<ServiceEndpoint>(), Tenant, Clock);
        Assert.AreEqual(0, s.Endpoints.Count);
    }

    [TestMethod]
    public void Create_preserves_endpoint_order()
    {
        var a = Ep("https://a.example.com");
        var b = Ep("https://b.example.com");
        var s = Service.Create("svc", "desc", Creator, Team, new[] { a, b }, Tenant, Clock);
        Assert.AreEqual("https://a.example.com", s.Endpoints[0].Url);
        Assert.AreEqual("https://b.example.com", s.Endpoints[1].Url);
    }

    [TestMethod]
    public void Create_throws_when_endpoints_exceed_50()
    {
        var many = Enumerable.Range(0, 51).Select(i => Ep($"https://h{i}.example.com")).ToArray();
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", "desc", Creator, Team, many, Tenant, Clock));
    }

    [TestMethod]
    public void Create_allows_exactly_50_endpoints()
    {
        var fifty = Enumerable.Range(0, 50).Select(i => Ep($"https://h{i}.example.com")).ToArray();
        var s = Service.Create("svc", "desc", Creator, Team, fifty, Tenant, Clock);
        Assert.AreEqual(50, s.Endpoints.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_display_name(string name) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create(name, "desc", Creator, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    public void Create_throws_on_display_name_over_128() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create(new string('x', 129), "desc", Creator, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_description(string desc) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", desc, Creator, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    public void Create_throws_on_description_over_4096() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", new string('x', 4097), Creator, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    public void Create_throws_on_empty_created_by() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", "desc", Guid.Empty, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    public void Create_throws_on_empty_team() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", "desc", Creator, Guid.Empty, new[] { Ep() }, Tenant, Clock));
}
```

> `Microsoft.Extensions.Time.Testing` (`FakeTimeProvider`) is already referenced by `Kartova.Catalog.Tests` (see `TestClocks.cs`). If the build can't resolve it, add the package ref before Step 4.

- [ ] **Step 2: Run tests to verify they fail**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -c Debug"`
Expected: FAIL — `Service` does not exist.

- [ ] **Step 3: Write the aggregate**

`src/Modules/Catalog/Kartova.Catalog.Domain/Service.cs`:

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

public sealed class Service : ITenantOwned, ITeamScopedResource
{
    private const int MaxEndpoints = 50;

    // Plain-Guid backing field for the PK so EF translates ORDER BY / WHERE without
    // a value converter (same pattern as Application._id).
    private Guid _id;
    private readonly List<ServiceEndpoint> _endpoints = new();

    public ServiceId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid TeamId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public HealthStatus Health { get; private set; } = HealthStatus.Unknown;
    public IReadOnlyList<ServiceEndpoint> Endpoints => _endpoints;
    public uint Version { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    private Service() { }   // EF

    private Service(
        ServiceId id, TenantId tenantId, string displayName, string description,
        Guid createdByUserId, Guid teamId, IEnumerable<ServiceEndpoint> endpoints, DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        DisplayName = displayName;
        Description = description;
        CreatedByUserId = createdByUserId;
        TeamId = teamId;
        CreatedAt = createdAt;
        _endpoints.AddRange(endpoints);
    }

    public static Service Create(
        string displayName, string description, Guid createdByUserId, Guid teamId,
        IEnumerable<ServiceEndpoint> endpoints, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(displayName, description, createdByUserId, teamId, endpoints, tenantId, clock.GetUtcNow());
    }

    /// <summary>Overload taking an explicit <paramref name="createdAt"/> — used by
    /// seeding/test fixtures that need deterministic ordering.</summary>
    public static Service Create(
        string displayName, string description, Guid createdByUserId, Guid teamId,
        IEnumerable<ServiceEndpoint> endpoints, TenantId tenantId, DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));
        if (teamId == Guid.Empty)
            throw new ArgumentException("teamId is required.", nameof(teamId));

        var list = endpoints?.ToList() ?? new List<ServiceEndpoint>();
        if (list.Count > MaxEndpoints)
            throw new ArgumentException($"a service may have at most {MaxEndpoints} endpoints.", nameof(endpoints));

        return new Service(ServiceId.New(), tenantId, displayName, description, createdByUserId, teamId, list, createdAt);
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Service display name must not be empty.", nameof(displayName));
        if (displayName.Length > 128)
            throw new ArgumentException("Service display name must be <= 128 characters.", nameof(displayName));
    }

    private static void ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Service description must not be empty.", nameof(description));
        if (description.Length > 4096)
            throw new ArgumentException("Service description must be <= 4096 characters.", nameof(description));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -c Debug"`
Expected: PASS (all `ServiceTests`).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/Service.cs \
        src/Modules/Catalog/Kartova.Catalog.Tests/ServiceTests.cs
git commit -m "feat(catalog): add Service aggregate with Create invariants (endpoints 0..50)"
```

---

### Task 3: Contracts + `ToResponse` mapping

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ServiceEndpointDto.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/RegisterServiceRequest.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ServiceResponse.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ServiceSortField.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ServiceResponseExtensions.cs`

**Interfaces:**
- Consumes: `Service` aggregate, `Protocol`, `HealthStatus`, `ServiceEndpoint` (Tasks 1–2); `UserDisplayInfo` (`Kartova.SharedKernel.AspNetCore`); `VersionEncoding` (`Kartova.SharedKernel.AspNetCore`).
- Produces: `RegisterServiceRequest(string DisplayName, string Description, Guid TeamId, IReadOnlyList<ServiceEndpointDto> Endpoints)`; `ServiceEndpointDto(string Url, Protocol Protocol)`; `ServiceResponse(...)` with init-only `CreatedBy`; `ServiceSortField { CreatedAt, DisplayName }`; `Service.ToResponse()` extension.

- [ ] **Step 1: Create the contract DTOs**

`ServiceEndpointDto.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record ServiceEndpointDto(string Url, Protocol Protocol);
```

`RegisterServiceRequest.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record RegisterServiceRequest(
    string DisplayName,
    string Description,
    Guid TeamId,
    IReadOnlyList<ServiceEndpointDto> Endpoints);
```

`ServiceResponse.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.Catalog.Contracts;

/// <summary>API response for a single catalog service. <see cref="CreatedBy"/> is
/// enriched by the read handlers via <c>IUserDirectory</c> (mirrors
/// <c>ApplicationResponse</c>); write-path handlers leave it null.</summary>
[ExcludeFromCodeCoverage]
public sealed record ServiceResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    Guid TeamId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    HealthStatus Health,
    IReadOnlyList<ServiceEndpointDto> Endpoints,
    string Version)
{
    public UserDisplayInfo? CreatedBy { get; init; }
}
```

`ServiceSortField.cs`:

```csharp
namespace Kartova.Catalog.Contracts;

/// <summary>Public sort-field allowlist for <c>GET /api/v1/catalog/services</c>.
/// ADR-0095.</summary>
public enum ServiceSortField
{
    CreatedAt,
    DisplayName,
}
```

- [ ] **Step 2: Write the failing mapping test**

Add to a new file `src/Modules/Catalog/Kartova.Catalog.Tests/ServiceResponseExtensionsTests.cs`:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ServiceResponseExtensionsTests
{
    [TestMethod]
    public void ToResponse_maps_all_fields_and_endpoints()
    {
        var tenant = new TenantId(Guid.NewGuid());
        var team = Guid.NewGuid();
        var creator = Guid.NewGuid();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-20T10:00:00Z"));
        var svc = Service.Create("orders-svc", "Orders.", creator, team,
            new[] { new ServiceEndpoint("https://api.example.com", Protocol.Grpc) }, tenant, clock);

        var resp = svc.ToResponse();

        Assert.AreEqual(svc.Id.Value, resp.Id);
        Assert.AreEqual(tenant.Value, resp.TenantId);
        Assert.AreEqual("orders-svc", resp.DisplayName);
        Assert.AreEqual(team, resp.TeamId);
        Assert.AreEqual(HealthStatus.Unknown, resp.Health);
        Assert.AreEqual(1, resp.Endpoints.Count);
        Assert.AreEqual("https://api.example.com", resp.Endpoints[0].Url);
        Assert.AreEqual(Protocol.Grpc, resp.Endpoints[0].Protocol);
        Assert.IsNull(resp.CreatedBy);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -c Debug"`
Expected: FAIL — `ToResponse` not defined.

- [ ] **Step 4: Write the extension**

`src/Modules/Catalog/Kartova.Catalog.Application/ServiceResponseExtensions.cs`:

```csharp
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.Catalog.Application;

public static class ServiceResponseExtensions
{
    public static ServiceResponse ToResponse(this Kartova.Catalog.Domain.Service svc) =>
        new(
            svc.Id.Value,
            svc.TenantId.Value,
            svc.DisplayName,
            svc.Description,
            svc.TeamId,
            svc.CreatedByUserId,
            svc.CreatedAt,
            svc.Health,
            svc.Endpoints.Select(e => new ServiceEndpointDto(e.Url, e.Protocol)).ToList(),
            VersionEncoding.Encode(svc.Version));
}
```

- [ ] **Step 5: Run test to verify it passes, then commit**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -c Debug"`
Expected: PASS.

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts/ServiceEndpointDto.cs \
        src/Modules/Catalog/Kartova.Catalog.Contracts/RegisterServiceRequest.cs \
        src/Modules/Catalog/Kartova.Catalog.Contracts/ServiceResponse.cs \
        src/Modules/Catalog/Kartova.Catalog.Contracts/ServiceSortField.cs \
        src/Modules/Catalog/Kartova.Catalog.Application/ServiceResponseExtensions.cs \
        src/Modules/Catalog/Kartova.Catalog.Tests/ServiceResponseExtensionsTests.cs
git commit -m "feat(catalog): add Service contracts + ToResponse mapping"
```

---

### Task 4: EF configuration, DbSet, and migration (jsonb endpoints + RLS)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfServiceConfiguration.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs`
- Create (generated, then hand-edit): `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<ts>_AddServices.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ServicePersistenceTests.cs`

**Interfaces:**
- Consumes: `Service`, `ServiceEndpoint`, `Protocol`, `HealthStatus` (Tasks 1–2).
- Produces: `EfServiceConfiguration.IdFieldName` const (`"_id"`); `CatalogDbContext.Services` `DbSet`; the `catalog_services` table.

- [ ] **Step 1: Write the EF configuration**

`src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfServiceConfiguration.cs`:

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfServiceConfiguration : IEntityTypeConfiguration<Service>
{
    internal const string IdFieldName = "_id";

    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.ToTable("catalog_services");

        b.Property<Guid>(IdFieldName)
            .HasField(IdFieldName)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasKey(IdFieldName);
        b.Ignore(x => x.Id);

        b.Property(x => x.TenantId)
            .HasConversion(v => v.Value, v => new TenantId(v))
            .HasColumnName("tenant_id")
            .IsRequired();
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_services_tenant_id");

        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.DisplayName })
            .HasDatabaseName("ix_catalog_services_tenant_id_display_name");

        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4096).IsRequired();
        b.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
        b.HasIndex(x => x.TeamId).HasDatabaseName("idx_catalog_services_team");
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property(x => x.Health)
            .HasColumnName("health")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .HasDefaultValue(HealthStatus.Unknown)
            .IsRequired();

        // Owned collection serialized to a single jsonb column. EF rehydrates each
        // element through the ServiceEndpoint(url, protocol) constructor (param names
        // match Url/Protocol). An empty collection round-trips as `[]`.
        b.OwnsMany(x => x.Endpoints, nav =>
        {
            nav.ToJson("endpoints");
            nav.Property(e => e.Url).HasJsonPropertyName("url");
            nav.Property(e => e.Protocol).HasConversion<short>().HasJsonPropertyName("protocol");
        });

        b.Property(x => x.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion()
            .IsConcurrencyToken();
    }
}
```

- [ ] **Step 2: Wire the DbSet + configuration**

In `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs`, add the `DbSet` after `Applications` (line ~20):

```csharp
public DbSet<Kartova.Catalog.Domain.Service> Services => Set<Kartova.Catalog.Domain.Service>();
```

and add the configuration in `OnModelCreating` after the `EfApplicationConfiguration` line:

```csharp
modelBuilder.ApplyConfiguration(new EfServiceConfiguration());
```

- [ ] **Step 3: Generate the migration**

Run (from repo root):

```bash
cmd //c "dotnet ef migrations add AddServices --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Migrator"
```

Expected: creates `Migrations/<ts>_AddServices.cs` + `.Designer.cs` and updates `CatalogDbContextModelSnapshot.cs`. Confirm the generated `Up` creates `catalog_services` with an `endpoints jsonb` column and `health smallint` default.

- [ ] **Step 4: Hand-add RLS to the migration**

Edit the generated `Up(...)` — append after the `CreateTable`/`CreateIndex` calls (mirror `AddApplications`):

```csharp
migrationBuilder.Sql(@"
ALTER TABLE catalog_services ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_services FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_services
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
```

and prepend to `Down(...)`:

```csharp
migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON catalog_services;
ALTER TABLE catalog_services DISABLE ROW LEVEL SECURITY;
");
```

- [ ] **Step 5: Write the persistence integration test**

`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ServicePersistenceTests.cs`:

```csharp
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.Catalog.IntegrationTests.Fixtures;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class ServicePersistenceTests
{
    private static PostgresFixture Pg { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        Pg = new PostgresFixture();
        await Pg.InitializeAsync();
    }

    [ClassCleanup]
    public static async Task ClassDone()
    {
        if (Pg is not null) await Pg.DisposeAsync();
    }

    [TestMethod]
    public async Task Endpoints_roundtrip_through_jsonb_and_empty_list_is_not_null()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(Pg.ConnectionString).Options;
        await using (var ctx = new CatalogDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        var tenant = new TenantId(Guid.NewGuid());
        // SET LOCAL the tenant guc so RLS lets the insert/select through (the test
        // talks to the DbContext directly, outside the API's tenant-scope middleware).
        var withEndpoints = Service.Create("svc-a", "with endpoints", Guid.NewGuid(), Guid.NewGuid(),
            new[] { new ServiceEndpoint("https://a.example.com", Protocol.Rest),
                    new ServiceEndpoint("https://b.example.com", Protocol.Grpc) },
            tenant, DateTimeOffset.UtcNow);
        var noEndpoints = Service.Create("svc-b", "no endpoints", Guid.NewGuid(), Guid.NewGuid(),
            Array.Empty<ServiceEndpoint>(), tenant, DateTimeOffset.UtcNow);

        await using (var ctx = new CatalogDbContext(options))
        {
            await ctx.Database.OpenConnectionAsync();
            await SetTenantAsync(ctx, tenant.Value);
            ctx.Services.AddRange(withEndpoints, noEndpoints);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new CatalogDbContext(options))
        {
            await ctx.Database.OpenConnectionAsync();
            await SetTenantAsync(ctx, tenant.Value);
            var a = await ctx.Services.SingleAsync(s => s.DisplayName == "svc-a");
            var b = await ctx.Services.SingleAsync(s => s.DisplayName == "svc-b");

            Assert.AreEqual(2, a.Endpoints.Count);
            Assert.AreEqual(Protocol.Grpc, a.Endpoints[1].Protocol);
            Assert.IsNotNull(b.Endpoints);
            Assert.AreEqual(0, b.Endpoints.Count);
            Assert.AreEqual(HealthStatus.Unknown, b.Health);
        }
    }

    private static async Task SetTenantAsync(CatalogDbContext ctx, Guid tenant)
    {
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SET app.current_tenant_id = '{tenant}'";
        await cmd.ExecuteNonQueryAsync();
    }
}
```

> If `SET app.current_tenant_id` errors as an unknown parameter, use `set_config('app.current_tenant_id', @t, false)` instead — match whatever `PostgresFixture` / existing tests use for the GUC. Confirm against `MigrationIntegrationTests`/`ListApplicationsPaginationTests` setup.

- [ ] **Step 6: Run the persistence test**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter ServicePersistenceTests -c Debug"`
Expected: PASS — endpoints round-trip; empty list materializes as a non-null empty collection.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfServiceConfiguration.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/ \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ServicePersistenceTests.cs
git commit -m "feat(catalog): persist Service with jsonb endpoints + RLS (AddServices migration)"
```

---

### Task 5: Register command, handler, and audit action

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/RegisterServiceCommand.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterServiceHandler.cs`

**Interfaces:**
- Consumes: `Service.Create`, `ServiceEndpoint` (Tasks 1–2); `ToResponse` (Task 3); `CatalogDbContext.Services` (Task 4); `ITenantContext`, `ICurrentUser`, `IAuditWriter`, `AuditEntry(string Action, string TargetType, string TargetId, IReadOnlyDictionary<string,string?>? Data)`.
- Produces: `RegisterServiceCommand(string DisplayName, string Description, Guid TeamId, IReadOnlyList<ServiceEndpointInput> Endpoints)` where `ServiceEndpointInput(string Url, Protocol Protocol)`; `RegisterServiceHandler.Handle(RegisterServiceCommand, CatalogDbContext, ITenantContext, ICurrentUser, IAuditWriter, CancellationToken) : Task<ServiceResponse>`; `CatalogAuditActions.ServiceRegistered`, `CatalogAuditTargetTypes.Service`.

> The command carries its own `ServiceEndpointInput` record (Application-layer, not the Contracts DTO) so the Application project need not depend on transport DTO wire concerns — the delegate maps `ServiceEndpointDto` → `ServiceEndpointInput`. `ServiceEndpointInput` lives in the command file.

- [ ] **Step 1: Add the audit constants**

Edit `src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs`:

```csharp
public const string ServiceRegistered = "service.registered";
```

(add inside `CatalogAuditActions`), and:

```csharp
public const string Service = "Service";
```

(add inside `CatalogAuditTargetTypes`).

- [ ] **Step 2: Create the command**

`src/Modules/Catalog/Kartova.Catalog.Application/RegisterServiceCommand.cs`:

```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>
/// Register a new <see cref="Kartova.Catalog.Domain.Service"/> in the current tenant.
/// Tenant id + created-by come from request context (ADR-0090); <c>TeamId</c> is the
/// required owning team (ADR-0103), validated by the delegate before dispatch.
/// </summary>
public sealed record RegisterServiceCommand(
    string DisplayName,
    string Description,
    Guid TeamId,
    IReadOnlyList<ServiceEndpointInput> Endpoints);

/// <summary>Transport-agnostic endpoint input for the register command.</summary>
public sealed record ServiceEndpointInput(string Url, Protocol Protocol);
```

- [ ] **Step 3: Write the handler**

`src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterServiceHandler.cs`:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="RegisterServiceCommand"/> (ADR-0093).
/// Tenant id + created-by come from <see cref="ITenantContext"/> / <see cref="ICurrentUser"/>;
/// the owning team id is validated by the delegate before dispatch. Audit row is written
/// in-transaction (fail-closed) before the response is returned.
/// </summary>
public sealed class RegisterServiceHandler
{
    private readonly TimeProvider _clock;

    public RegisterServiceHandler(TimeProvider clock) => _clock = clock;

    public async Task<ServiceResponse> Handle(
        RegisterServiceCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var endpoints = cmd.Endpoints.Select(e => new ServiceEndpoint(e.Url, e.Protocol));
        var svc = Service.Create(
            cmd.DisplayName, cmd.Description, user.UserId, cmd.TeamId, endpoints, tenant.Id, _clock);

        db.Services.Add(svc);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ServiceRegistered,
            CatalogAuditTargetTypes.Service,
            svc.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = svc.DisplayName,
                ["teamId"] = svc.TeamId.ToString(),
                ["endpointCount"] = svc.Endpoints.Count.ToString(),
            }), ct);

        return svc.ToResponse();
    }
}
```

> No unit test here — the register path is exercised end-to-end by the real-seam integration tests in Task 8/9 (handler depends on `CatalogDbContext`, `ITenantContext`, `IAuditWriter`; mocking them would test the mock, not the seam). The mapping `ServiceEndpoint` validation is already unit-covered (Task 1).

- [ ] **Step 4: Build to verify it compiles**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure -c Debug"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/RegisterServiceCommand.cs \
        src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterServiceHandler.cs
git commit -m "feat(catalog): RegisterServiceHandler + service.registered audit action"
```

---

### Task 6: Read handlers — get-by-id, list, sort specs

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/GetServiceByIdQuery.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ListServicesQuery.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ServiceSortSpecs.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetServiceByIdHandler.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListServicesHandler.cs`

**Interfaces:**
- Consumes: `Service`, `ServiceResponse`, `ServiceSortField` (Tasks 1–3); `EfServiceConfiguration.IdFieldName` (Task 4); `IUserDirectory` (`Kartova.SharedKernel.Identity`); `SortSpec<T>`, `SortOrder`, `CursorPage<T>`, `InvalidSortFieldException` (`Kartova.SharedKernel.Pagination`); `QueryablePagingExtensions.ToCursorPagedAsync` (`Kartova.SharedKernel.Postgres.Pagination`); `ToResponse` (Task 3).
- Produces: `GetServiceByIdQuery(Guid Id)`; `ListServicesQuery(ServiceSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit)`; `ServiceSortSpecs.{IdSelector, IdEquals, Resolve, AllowedFieldNames}`; `GetServiceByIdHandler.Handle(...) : Task<ServiceResponse?>`; `ListServicesHandler.Handle(...) : Task<CursorPage<ServiceResponse>>`.

- [ ] **Step 1: Create the queries**

`GetServiceByIdQuery.cs`:

```csharp
namespace Kartova.Catalog.Application;

/// <summary>Fetch one Service by id within the current tenant scope (RLS-filtered).</summary>
public sealed record GetServiceByIdQuery(Guid Id);
```

`ListServicesQuery.cs`:

```csharp
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>List services visible to the current tenant (RLS-filtered). ADR-0095.</summary>
public sealed record ListServicesQuery(
    ServiceSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit);
```

- [ ] **Step 2: Create the sort specs**

`src/Modules/Catalog/Kartova.Catalog.Infrastructure/ServiceSortSpecs.cs`:

```csharp
using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using DomainService = Kartova.Catalog.Domain.Service;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Per-resource sort allowlist for the Services list endpoint (ADR-0095 §5).</summary>
internal static class ServiceSortSpecs
{
    public static readonly Expression<Func<DomainService, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfServiceConfiguration.IdFieldName);

    public static readonly SortSpec<DomainService> CreatedAt = new("createdAt", x => x.CreatedAt);
    public static readonly SortSpec<DomainService> DisplayName = new("displayName", x => x.DisplayName);

    public static readonly IReadOnlyList<string> AllowedFieldNames = [CreatedAt.FieldName, DisplayName.FieldName];

    public static Expression<Func<DomainService, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, EfServiceConfiguration.IdFieldName) == id;

    public static SortSpec<DomainService> Resolve(ServiceSortField field) => field switch
    {
        ServiceSortField.CreatedAt => CreatedAt,
        ServiceSortField.DisplayName => DisplayName,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
```

- [ ] **Step 3: Create the get-by-id handler**

`src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetServiceByIdHandler.cs`:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="GetServiceByIdQuery"/>. Returns null when the row
/// is invisible in the current tenant scope (RLS auto-filters). Enriches
/// <c>CreatedBy</c> via <see cref="IUserDirectory"/> (mirrors GetApplicationByIdHandler).</summary>
public sealed class GetServiceByIdHandler(IUserDirectory directory)
{
    public async Task<ServiceResponse?> Handle(
        GetServiceByIdQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var svc = await db.Services.FirstOrDefaultAsync(ServiceSortSpecs.IdEquals(q.Id), ct);
        if (svc is null) return null;

        var creator = await directory.GetAsync(svc.CreatedByUserId, ct);
        return svc.ToResponse() with { CreatedBy = creator };
    }
}
```

- [ ] **Step 4: Create the list handler**

`src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListServicesHandler.cs`:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using DomainService = Kartova.Catalog.Domain.Service;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="ListServicesQuery"/>. RLS scopes the result set;
/// keyset pagination via ToCursorPagedAsync (ADR-0095). Each page row is enriched with
/// the creator display name in one batched IUserDirectory round trip (mirrors
/// ListApplicationsHandler).</summary>
public sealed class ListServicesHandler(IUserDirectory directory)
{
    private static readonly Func<DomainService, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<ServiceResponse>> Handle(
        ListServicesQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var spec = ServiceSortSpecs.Resolve(q.SortBy);

        var page = await db.Services
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ServiceSortSpecs.IdSelector, IdExtractor, ct);

        var creatorIds = new HashSet<Guid>(page.Items.Select(s => s.CreatedByUserId));
        var creators = await directory.GetManyAsync(creatorIds, ct);

        var items = page.Items
            .Select(r =>
            {
                var resp = r.ToResponse();
                return creators.TryGetValue(r.CreatedByUserId, out var creator)
                    ? resp with { CreatedBy = creator }
                    : resp;
            })
            .ToList();
        return new CursorPage<ServiceResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
```

> Confirm the `ToCursorPagedAsync` overload signature against `ListApplicationsHandler` — it there passes `expectedFilters`. Services have no filters this slice, so call the overload without `expectedFilters` (or pass an empty dictionary if the only overload requires it). Match the actual signature in `Kartova.SharedKernel.Postgres.Pagination.QueryablePagingExtensions`.

- [ ] **Step 5: Build to verify it compiles**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure -c Debug"`
Expected: 0 warnings, 0 errors. (Behavior is verified by the integration tests in Task 9.)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/GetServiceByIdQuery.cs \
        src/Modules/Catalog/Kartova.Catalog.Application/ListServicesQuery.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/ServiceSortSpecs.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetServiceByIdHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListServicesHandler.cs
git commit -m "feat(catalog): Service read handlers (get-by-id + cursor list) + sort specs"
```

---

### Task 7: Permission + role map

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs`
- Test: `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs`

**Interfaces:**
- Produces: `KartovaPermissions.CatalogServicesRegister = "catalog.services.register"` (added to `All`); `CatalogServicesRegister` granted to `Member` + `OrgAdmin`, denied to `Viewer`.

- [ ] **Step 1: Write the failing role-map test**

Add to `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs` (inside the existing test class, following the existing `CatalogApplicationsRegister` assertions):

```csharp
[TestMethod]
public void Member_and_OrgAdmin_can_register_services_but_Viewer_cannot()
{
    var member = KartovaRolePermissions.ForRole(KartovaRoles.Member);
    var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
    var viewer = KartovaRolePermissions.ForRole(KartovaRoles.Viewer);

    Assert.IsTrue(member.Contains(KartovaPermissions.CatalogServicesRegister));
    Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.CatalogServicesRegister));
    Assert.IsFalse(viewer.Contains(KartovaPermissions.CatalogServicesRegister));
}

[TestMethod]
public void CatalogServicesRegister_is_in_the_All_set() =>
    Assert.IsTrue(KartovaPermissions.All.Contains(KartovaPermissions.CatalogServicesRegister));
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests -c Debug"`
Expected: FAIL — `CatalogServicesRegister` does not exist.

- [ ] **Step 3: Add the permission constant**

In `KartovaPermissions.cs`, add after `CatalogApplicationsLifecycleReverse` (line ~11):

```csharp
public const string CatalogServicesRegister = "catalog.services.register";
```

and add `CatalogServicesRegister,` to the `All` initializer array (after the other catalog entries).

- [ ] **Step 4: Map it to roles**

In `KartovaRolePermissions.cs`, add `KartovaPermissions.CatalogServicesRegister,` to BOTH the `Member` array (after `CatalogApplicationsLifecycleForward`) and the `OrgAdmin` array (after `CatalogApplicationsLifecycleReverse`). Do NOT add it to `Viewer`.

- [ ] **Step 5: Run test to verify it passes**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests -c Debug"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs \
        src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs \
        tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs
git commit -m "feat(auth): add catalog.services.register permission (Member + OrgAdmin)"
```

---

### Task 8: Endpoint delegates + module wiring + first integration test

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EndpointResultExtensions.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterServiceTests.cs`

**Interfaces:**
- Consumes: handlers + commands from Tasks 5–6; `IOrganizationTeamExistenceChecker` (`Kartova.SharedKernel.Multitenancy`); `AuthorizeTargetTeamAsync` helper (private, already in `CatalogEndpointDelegates`); `KartovaPermissions.CatalogServicesRegister`, `CatalogRead`; `CursorListBinding.Bind<T>`, `ProblemTypes.InvalidTeam`.
- Produces: routes `POST /api/v1/catalog/services`, `GET /api/v1/catalog/services/{id:guid}`, `GET /api/v1/catalog/services`; `EndpointResultExtensions.ServiceNotFound()`.

- [ ] **Step 1: Add a ServiceNotFound helper**

In `EndpointResultExtensions.cs`, add a method mirroring `ApplicationNotFound()` (open the file to copy the exact body — it returns a 404 `Results.Problem` with `ProblemTypes.ResourceNotFound`):

```csharp
public static IResult ServiceNotFound() =>
    Results.Problem(
        type: ProblemTypes.ResourceNotFound,
        title: "Service not found",
        detail: "No service with the supplied id exists in the current tenant.",
        statusCode: StatusCodes.Status404NotFound);
```

> Match the exact `ProblemTypes` member and argument style used by the existing `ApplicationNotFound()` in the same file.

- [ ] **Step 2: Add the three delegates**

Append to `CatalogEndpointDelegates` (the `using` for `RegisterServiceCommand`/`ServiceEndpointInput` is `Kartova.Catalog.Application`, already imported):

```csharp
internal static async Task<IResult> RegisterServiceAsync(
    [FromBody] RegisterServiceRequest request,
    RegisterServiceHandler handler,
    CatalogDbContext db,
    ITenantContext tenant,
    ClaimsPrincipal caller,
    ICurrentUser currentUser,
    IAuthorizationService auth,
    IOrganizationTeamExistenceChecker teamChecker,
    IAuditWriter audit,
    CancellationToken ct)
{
    // ADR-0103: a new service requires an existing owning team in the tenant.
    // RLS-scoped checker → a cross-tenant id resolves as "not found" (same 422 branch).
    if (!await teamChecker.ExistsAsync(request.TeamId, ct))
    {
        return Results.Problem(
            type: ProblemTypes.InvalidTeam,
            title: "Invalid team",
            detail: "The supplied teamId does not resolve to a team in the current tenant.",
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    // Target-team membership gate: a non-OrgAdmin caller cannot register into a team
    // they do not belong to (reuses the shared ApplicationTeamScoped policy).
    if (await AuthorizeTargetTeamAsync(auth, caller, request.TeamId) is { } forbidden)
        return forbidden;

    var endpoints = (request.Endpoints ?? Array.Empty<ServiceEndpointDto>())
        .Select(e => new ServiceEndpointInput(e.Url, e.Protocol))
        .ToList();

    var response = await handler.Handle(
        new RegisterServiceCommand(request.DisplayName, request.Description, request.TeamId, endpoints),
        db, tenant, currentUser, audit, ct);

    return Results.Created($"/api/v1/catalog/services/{response.Id}", response);
}

internal static async Task<IResult> GetServiceByIdAsync(
    Guid id,
    GetServiceByIdHandler handler,
    CatalogDbContext db,
    CancellationToken ct)
{
    var resp = await handler.Handle(new GetServiceByIdQuery(id), db, ct);
    if (resp is null) return EndpointResultExtensions.ServiceNotFound();
    return Results.Ok(resp).WithEtag(resp.Version);
}

internal static async Task<IResult> ListServicesAsync(
    [FromQuery] string? sortBy,
    [FromQuery] string? sortOrder,
    [FromQuery] string? cursor,
    [FromQuery] string? limit,
    ListServicesHandler handler,
    CatalogDbContext db,
    CancellationToken ct)
{
    var (parsedSortBy, parsedSortOrder, effectiveLimit) =
        CursorListBinding.Bind<ServiceSortField>(sortBy, sortOrder, limit, ServiceSortSpecs.AllowedFieldNames);

    var query = new ListServicesQuery(
        SortBy: parsedSortBy ?? ServiceSortField.CreatedAt,
        SortOrder: parsedSortOrder ?? SortOrder.Desc,
        Cursor: cursor,
        Limit: effectiveLimit);

    var page = await handler.Handle(query, db, ct);
    return Results.Ok(page);
}
```

> `CursorListBinding.Bind<T>` requires `T : struct, Enum` — `ServiceSortField` qualifies. Confirm the exact tuple return shape against the existing `ListApplicationsAsync` call.

- [ ] **Step 3: Wire endpoints + DI in CatalogModule**

In `CatalogModule.MapEndpoints`, after the applications routes, add:

```csharp
tenant.MapPost("/services", CatalogEndpointDelegates.RegisterServiceAsync)
      .RequireAuthorization(KartovaPermissions.CatalogServicesRegister)
      .WithName("RegisterService")
      .Produces<ServiceResponse>(StatusCodes.Status201Created)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .ProducesProblem(StatusCodes.Status403Forbidden)
      .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
tenant.MapGet("/services/{id:guid}", CatalogEndpointDelegates.GetServiceByIdAsync)
      .RequireAuthorization(KartovaPermissions.CatalogRead)
      .WithName("GetServiceById")
      .Produces<ServiceResponse>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status404NotFound);
tenant.MapGet("/services", CatalogEndpointDelegates.ListServicesAsync)
      .RequireAuthorization(KartovaPermissions.CatalogRead)
      .WithName("ListServices")
      .Produces<CursorPage<ServiceResponse>>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
```

In `CatalogModule.RegisterServices`, after the application handler registrations, add:

```csharp
services.AddScoped<RegisterServiceHandler>();
services.AddScoped<GetServiceByIdHandler>();
services.AddScoped<ListServicesHandler>();
```

- [ ] **Step 4: Write the first integration test (RED → drives wiring)**

`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterServiceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class RegisterServiceTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static object Body(Guid teamId, params (string Url, Protocol Protocol)[] eps) => new
    {
        displayName = "orders-svc",
        description = "Order service.",
        teamId,
        endpoints = eps.Select(e => new { url = e.Url, protocol = e.Protocol }).ToArray(),
    };

    [TestMethod]
    public async Task POST_with_valid_payload_returns_201_and_echoes_endpoints_and_unknown_health()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services",
            Body(teamId, ("https://api.example.com/v1", Protocol.Rest), ("grpc://api.example.com", Protocol.Grpc)));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("orders-svc", body!.DisplayName);
        Assert.AreEqual(HealthStatus.Unknown, body.Health);
        Assert.AreEqual(2, body.Endpoints.Count);
        Assert.AreEqual(Protocol.Grpc, body.Endpoints[1].Protocol);
        Assert.AreEqual(teamId, body.TeamId);
    }

    [TestMethod]
    public async Task POST_with_zero_endpoints_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team 0");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", Body(teamId));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(0, body!.Endpoints.Count);
    }

    [TestMethod]
    public async Task POST_without_token_returns_401()
    {
        using var anon = Fx.CreateAnonymousClient();
        var resp = await anon.PostAsJsonAsync("/api/v1/catalog/services", Body(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
```

- [ ] **Step 5: Run the test — fail then pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter RegisterServiceTests -c Debug"`
Expected: PASS after Steps 1–3 land. (If routes are missing it 404s — confirm wiring.)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/EndpointResultExtensions.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterServiceTests.cs
git commit -m "feat(catalog): wire /catalog/services endpoints (register + get + list)"
```

---

### Task 9: Negative-path, read, pagination, and permission-matrix integration tests

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterServiceTests.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetServiceByIdTests.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListServicesPaginationTests.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs`

**Interfaces:**
- Consumes: everything wired in Task 8; fixture helpers `Fx.CreateAuthenticatedClientAsync`, `Fx.SeedTeamInOrganizationAsync`, `Fx.TenantIdForEmail`, `Fx.GetSubClaimAsync`, `Fx.SeedTeamMembershipAsync`, `Fx.DeleteTeamsForTenantAsync`, `KartovaApiFixtureBase.{TenantFor, WireJson}`.

- [ ] **Step 1: Add register negative-path tests**

Append to `RegisterServiceTests`:

```csharp
[TestMethod]
public async Task POST_with_empty_display_name_returns_400()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team 400");
    var resp = await client.PostAsJsonAsync("/api/v1/catalog/services",
        new { displayName = "", description = "d", teamId, endpoints = Array.Empty<object>() });
    Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
}

[TestMethod]
public async Task POST_with_relative_endpoint_url_returns_400()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team Url");
    var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
    {
        displayName = "svc", description = "d", teamId,
        endpoints = new[] { new { url = "/relative/path", protocol = Protocol.Rest } },
    });
    Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
}

[TestMethod]
public async Task POST_with_unknown_team_returns_422()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", Body(Guid.NewGuid()));
    Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
}

[TestMethod]
public async Task POST_with_51_endpoints_returns_400()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team 51");
    var eps = Enumerable.Range(0, 51).Select(i => ($"https://h{i}.example.com", Protocol.Rest)).ToArray();
    var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", Body(teamId, eps));
    Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
}
```

- [ ] **Step 2: Run register tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter RegisterServiceTests -c Debug"`
Expected: PASS (all register cases).

- [ ] **Step 3: Write get-by-id tests**

`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetServiceByIdTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class GetServiceByIdTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static async Task<ServiceResponse> RegisterAsync(HttpClient client, string email)
    {
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(email), "Get Team");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = "get-svc", description = "d", teamId,
            endpoints = new[] { new { url = "https://api.example.com", protocol = Protocol.Rest } },
        });
        Assert.IsTrue(resp.IsSuccessStatusCode);
        return (await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    [TestMethod]
    public async Task GET_returns_200_for_service_in_same_tenant()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var created = await RegisterAsync(client, OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/services/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(created.Id, body!.Id);
    }

    [TestMethod]
    public async Task GET_returns_404_for_nonexistent_id()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/services/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_returns_404_for_other_tenants_id()
    {
        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var created = await RegisterAsync(orgA, OrgAUser);
        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await orgB.GetAsync($"/api/v1/catalog/services/{created.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
```

- [ ] **Step 4: Write list + pagination tests**

`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListServicesPaginationTests.cs` — model it on the existing `ListApplicationsPaginationTests.cs` (open that file and mirror its structure):

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class ListServicesPaginationTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static async Task SeedAsync(HttpClient client, Guid teamId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
            {
                displayName = $"list-svc-{i:D2}", description = "d", teamId,
                endpoints = Array.Empty<object>(),
            });
            Assert.IsTrue(resp.IsSuccessStatusCode);
        }
    }

    [TestMethod]
    public async Task GET_returns_cursor_page_envelope_and_paginates_forward()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "List Team");
        await SeedAsync(client, teamId, 3);

        var firstResp = await client.GetAsync("/api/v1/catalog/services?sortBy=displayName&sortOrder=asc&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, firstResp.StatusCode);
        var first = await firstResp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, first!.Items.Count);
        Assert.IsNotNull(first.NextCursor);

        var nextResp = await client.GetAsync(
            $"/api/v1/catalog/services?sortBy=displayName&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        var next = await nextResp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(next!.Items.Count >= 1);
    }

    [TestMethod]
    public async Task GET_with_invalid_sortBy_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/catalog/services?sortBy=bogusField");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }
}
```

> Open `ListApplicationsPaginationTests.cs` first and copy its exact assertion idioms (cursor escaping, envelope property names, any seeding teardown). The structure above is the shape; align details to the existing file so both suites stay consistent.

- [ ] **Step 5: Add the 3 permission-matrix rows**

In `CatalogPermissionMatrixTests.cs`, add to the `Endpoints` array:

```csharp
(HttpMethod.Post, "/api/v1/catalog/services",        KartovaPermissions.CatalogServicesRegister),
(HttpMethod.Get,  "/api/v1/catalog/services",        KartovaPermissions.CatalogRead),
(HttpMethod.Get,  "/api/v1/catalog/services/{id}",   KartovaPermissions.CatalogRead),
```

Then extend `AttachShapeValidBody` to give the services POST a shape-valid body:

```csharp
else if (method == HttpMethod.Post && pathTemplate == "/api/v1/catalog/services")
{
    req.Content = JsonContent.Create(new
    {
        displayName = "Matrix Svc",
        description = "Matrix shape body.",
        teamId,                      // the seeded matrix team (in scope of the test method)
        endpoints = Array.Empty<object>(),
    });
}
```

> `AttachShapeValidBody` is `static` and has no `teamId` in scope. Mirror however the test threads the seeded team to the body — simplest is to make `teamId` a field set during arrange, or pass it into `AttachShapeValidBody`. Adjust the helper signature to accept `Guid teamId` and pass it at the call site (the matrix loop already has `teamId` from `SeedTeamInOrganizationAsync`). The `{id}` GET row reuses the seeded service id — register one service in the arrange block alongside the seeded app, or reuse the app-seeding pattern with a services POST.

- [ ] **Step 6: Run the full Catalog integration suite**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests -c Debug"`
Expected: PASS (register, get-by-id, list pagination, persistence, permission matrix, plus all pre-existing application suites).

> If a Docker named-pipe `TimeoutException` flakes one assembly under container saturation, re-run that assembly in isolation before treating it as red (known transient — see project memory).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/
git commit -m "test(catalog): Service register negatives, get-by-id, pagination, permission matrix"
```

---

### Task 10: Full verification + DoD gates + checklist + PR

**Files:**
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Full solution build (warnings-as-errors)**

Run: `cmd //c "dotnet build Kartova.slnx -c Debug"`
Expected: 0 warnings, 0 errors. Fix any before proceeding.

- [ ] **Step 2: Full test suite**

Run: `cmd //c "dotnet test Kartova.slnx -c Debug"`
Expected: all green — unit + architecture (`EndpointRouteRules` picks up the 3 new named routes; `ContractsCoverageRules` validates the new DTO `[ExcludeFromCodeCoverage]`) + integration.

- [ ] **Step 3: Run gates 5–9 (per CLAUDE.md DoD)**

Run, in order, against the branch diff, addressing should-fix items or noting skips:
- `/simplify` (gate 5)
- `/misc:mutation-sentinel` → `/misc:test-generator` (gate 6 — **blocking**: diff touches Domain `Service`/`ServiceEndpoint` + Application/Infra handlers; target ≥80%, document survivors)
- `/superpowers:requesting-code-review` (gate 7)
- `/pr-review-toolkit:review-pr` (gate 8)
- `/deep-review` (gate 9)

- [ ] **Step 4: Update the checklist**

In `docs/product/CHECKLIST.md`:
- Tick `- [x] E-02.F-02.S-01 — Register service with endpoints and protocol` with a dated note (mirror the Application S-01 note style: branch, jsonb endpoints 0..50, health defaults Unknown, deferrals).
- Bump Phase 1 progress `13/60 → 14/60` and Total `21/212 → 22/212`.
- Update `**Last updated:**` to the merge date.

- [ ] **Step 5: Terminal re-verify (DoD)**

After gates 5–9 may have applied fixes, re-run:
Run: `cmd //c "dotnet build Kartova.slnx -c Debug"` then `cmd //c "dotnet test Kartova.slnx -c Debug"`
Expected: both green.

- [ ] **Step 6: Pre-push CI mirror**

Run: `scripts/ci-local.sh` (Release build+test, web image, helm/stryker as CI runs them). The `images` job rebuilds the migrator image, which now carries the `AddServices` migration.
Expected: green. If a failure reproduces neither locally nor on re-run, it's a flaky test — fix determinism, don't re-push blindly.

- [ ] **Step 7: Commit checklist + push + open PR**

```bash
git add docs/product/CHECKLIST.md
git commit -m "docs(catalog): mark E-02.F-02.S-01 (Service entity) complete"
git push -u origin feat/catalog-service-entity
```

Open the PR with the spec + this plan linked as context. PR body: summary of the Service slice, the eight blocking gates' evidence, and the explicit deferrals (S-02 detail/health/consumers, edit/lifecycle, relationships).

---

## Self-Review

**1. Spec coverage:**
- §3 #1 sibling aggregate → Task 2. #2 scope (POST+GET+LIST) → Tasks 5,6,8. #3 owning team + membership → Task 8 delegate. #4 no Lifecycle → Task 2 (none added). #5 endpoints 0..50 → Task 2 invariant + Tasks 2/9 tests. #6/#7 ServiceEndpoint + Protocol → Task 1. #8 Health default Unknown, no write path → Tasks 2/4. #9 jsonb → Task 4. #10 domain-invariant validation → Task 2. #11 tenant/createdBy from context → Task 5 handler. #12 permission → Task 7. #13 audit → Task 5. #14 EF mechanics → Task 4.
- §7 gate-5 artifacts: domain units → Tasks 1–3; register real-seam happy+negatives → Tasks 8–9; get-by-id → Task 9; list pagination → Task 9; permission matrix + role map → Tasks 7,9; container build → Task 10 ci-local. All present.

**2. Placeholder scan:** No TBD/TODO. The `>` callouts are verification instructions pointing at named existing files (`ListApplicationsPaginationTests`, `QueryablePagingExtensions`, `EndpointResultExtensions`), not deferred work — each names the exact signature/idiom to match. No "add error handling"-style hand-waving (validation is concrete domain invariants with shown code).

**3. Type consistency:**
- `Service.Create(displayName, description, createdByUserId, teamId, endpoints, tenantId, clock)` — identical across Tasks 2, 3 (test), 5 (handler).
- `ServiceEndpoint(string url, Protocol protocol)` — Tasks 1, 3, 4, 5.
- `RegisterServiceCommand(DisplayName, Description, TeamId, IReadOnlyList<ServiceEndpointInput>)` + `ServiceEndpointInput(Url, Protocol)` — Task 5 defines, Task 8 consumes.
- `ServiceResponse` 10-field shape + init `CreatedBy` — Task 3 defines, Tasks 6/8/9 consume.
- `EfServiceConfiguration.IdFieldName` (`"_id"`) — Task 4 defines, Task 6 (`ServiceSortSpecs`) consumes.
- `CatalogAuditActions.ServiceRegistered` / `CatalogAuditTargetTypes.Service` — Task 5 defines + uses.
- `KartovaPermissions.CatalogServicesRegister` — Task 7 defines, Tasks 8/9 consume.
- `EndpointResultExtensions.ServiceNotFound()` — Task 8 defines + uses.
- Handler method names `Handle(...)` and return types (`Task<ServiceResponse>`, `Task<ServiceResponse?>`, `Task<CursorPage<ServiceResponse>>`) consistent Tasks 5–6 ↔ 8.

No inconsistencies found.
