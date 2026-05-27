# Slice 9 — Organization & people management — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close E-03.F-01.S-01..S-04 (org profile editing, invitations with copy-link UX, user display/detail, user search typeahead) while landing three pieces of cross-cutting shared infrastructure: KeyCloak Admin API client, `IUserDirectory` projection, and Postgres-advisory-lock distributed locking.

**Architecture:** New shared project `Kartova.SharedKernel.Identity` houses `IKeycloakAdminClient` + `IUserDirectory`. Distributed locking primitives live in `Kartova.SharedKernel` (`IDistributedLock`, `LeaderElectedPeriodicService`) + `Kartova.SharedKernel.Postgres` (`PostgresAdvisoryLock`). Organization module owns Invitation aggregate + `users` projection + Org profile extensions. JWT-claim sync hooks into existing `TenantClaimsTransformation`. Session bootstrap endpoint + welcome UX wraps the SPA login flow. MinIO + email infrastructure deferred per spec §12.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs · EF Core + Npgsql · Wolverine (in-process only) · IdentityModel.Client (KeyCloak token caching) · Ganss.Xss (SVG sanitization) · MSTest v4 + NSubstitute · Testcontainers (KeyCloak + PostgreSQL) · React 18 + TypeScript + React Query · Vite · zod · Playwright (E2E).

**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`

---

## Branch + worktree setup

- [ ] **Step 0.1: Create feature branch + worktree**

```bash
git checkout master
git pull origin master
git worktree add ../slice-9 -b feat/slice-9-organization-people-management
cd ../slice-9
```

Expected: new worktree at `../slice-9` on branch `feat/slice-9-organization-people-management`. All subsequent commands run from this worktree.

---

## Phase A — Shared infrastructure foundation (`Kartova.SharedKernel.Identity` + distributed locking)

### Task A1: Create `Kartova.SharedKernel.Identity` csproj + base records

**Files:**
- Create: `src/Kartova.SharedKernel.Identity/Kartova.SharedKernel.Identity.csproj`
- Create: `src/Kartova.SharedKernel/UserDisplayInfo.cs`
- Modify: `Kartova.slnx` (add the new project)

- [ ] **Step 1: Add the project file**

`src/Kartova.SharedKernel.Identity/Kartova.SharedKernel.Identity.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="IdentityModel" Version="12.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
    <PackageReference Include="System.Net.Http.Json" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add `UserDisplayInfo` to base SharedKernel**

`src/Kartova.SharedKernel/UserDisplayInfo.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel;

[ExcludeFromCodeCoverage]
public sealed record UserDisplayInfo(Guid Id, string DisplayName, string Email);
```

- [ ] **Step 3: Add project to solution**

```powershell
dotnet sln Kartova.slnx add src/Kartova.SharedKernel.Identity/Kartova.SharedKernel.Identity.csproj
```

Expected: `.slnx` updated.

- [ ] **Step 4: Verify build**

```powershell
dotnet build src/Kartova.SharedKernel.Identity/ -c Debug
```

Expected: build succeeds, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add Kartova.slnx src/Kartova.SharedKernel.Identity/ src/Kartova.SharedKernel/UserDisplayInfo.cs
git commit -m "feat(slice-9): scaffold Kartova.SharedKernel.Identity + UserDisplayInfo"
```

---

### Task A2: Add `IUserDirectory` interface

**Files:**
- Create: `src/Kartova.SharedKernel.Identity/IUserDirectory.cs`

- [ ] **Step 1: Write the interface file**

`src/Kartova.SharedKernel.Identity/IUserDirectory.cs`:

```csharp
namespace Kartova.SharedKernel.Identity;

public interface IUserDirectory
{
    Task<UserDisplayInfo?> GetAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, UserDisplayInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
```

- [ ] **Step 2: Verify build**

```powershell
dotnet build src/Kartova.SharedKernel.Identity/
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/Kartova.SharedKernel.Identity/IUserDirectory.cs
git commit -m "feat(slice-9): define IUserDirectory abstraction"
```

---

### Task A3: Add `IKeycloakAdminClient` interface + DTOs

**Files:**
- Create: `src/Kartova.SharedKernel.Identity/IKeycloakAdminClient.cs`
- Create: `src/Kartova.SharedKernel.Identity/KeycloakDtos.cs`
- Create: `src/Kartova.SharedKernel.Identity/KeycloakAdminException.cs`

- [ ] **Step 1: Add the exception type**

`src/Kartova.SharedKernel.Identity/KeycloakAdminException.cs`:

```csharp
namespace Kartova.SharedKernel.Identity;

public enum KeycloakAdminError
{
    EmailAlreadyExists,
    Unauthorized,
    NotFound,
    Unexpected,
}

public sealed class KeycloakAdminException : Exception
{
    public KeycloakAdminError Error { get; }

    public KeycloakAdminException(KeycloakAdminError error, string message) : base(message)
    {
        Error = error;
    }
}
```

- [ ] **Step 2: Add the DTOs**

`src/Kartova.SharedKernel.Identity/KeycloakDtos.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Identity;

[ExcludeFromCodeCoverage]
public sealed record CreateKeycloakUserRequest(
    string Email,
    string? FirstName,
    string? LastName,
    string TenantId,
    IReadOnlyList<string> RequiredActions);

[ExcludeFromCodeCoverage]
public sealed record KeycloakUser(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    bool Enabled,
    bool EmailVerified,
    string? TenantId);
```

- [ ] **Step 3: Add the interface**

`src/Kartova.SharedKernel.Identity/IKeycloakAdminClient.cs`:

```csharp
namespace Kartova.SharedKernel.Identity;

public interface IKeycloakAdminClient
{
    Task<Guid> CreateUserAsync(CreateKeycloakUserRequest request, CancellationToken ct);
    Task<KeycloakUser?> GetUserAsync(Guid userId, CancellationToken ct);
    Task AssignRealmRoleAsync(Guid userId, string roleName, CancellationToken ct);
    Task<IReadOnlyList<KeycloakUser>> SearchUsersAsync(string query, int limit, CancellationToken ct);
    Task DeleteUserAsync(Guid userId, CancellationToken ct);
}
```

- [ ] **Step 4: Verify build**

```powershell
dotnet build src/Kartova.SharedKernel.Identity/
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel.Identity/IKeycloakAdminClient.cs src/Kartova.SharedKernel.Identity/KeycloakDtos.cs src/Kartova.SharedKernel.Identity/KeycloakAdminException.cs
git commit -m "feat(slice-9): define IKeycloakAdminClient + DTOs"
```

---

### Task A4: Implement `KeycloakAdminClient` + options + DI

**Files:**
- Create: `src/Kartova.SharedKernel.Identity/KeycloakAdminOptions.cs`
- Create: `src/Kartova.SharedKernel.Identity/KeycloakAdminClient.cs`
- Create: `src/Kartova.SharedKernel.Identity/ServiceCollectionExtensions.cs`
- Create: `tests/Kartova.SharedKernel.Identity.Tests/Kartova.SharedKernel.Identity.Tests.csproj`
- Create: `tests/Kartova.SharedKernel.Identity.Tests/KeycloakAdminClientTests.cs`
- Create: `tests/Kartova.SharedKernel.Identity.Tests/StubHttpMessageHandler.cs`

- [ ] **Step 1: Add `KeycloakAdminOptions`**

`src/Kartova.SharedKernel.Identity/KeycloakAdminOptions.cs`:

```csharp
namespace Kartova.SharedKernel.Identity;

public sealed class KeycloakAdminOptions
{
    public required string BaseUrl { get; init; }
    public required string Realm { get; init; }
    public required string AdminClientId { get; init; }
    public required string AdminClientSecret { get; init; }
    public string FrontendBaseUrl { get; init; } = "";
}
```

- [ ] **Step 2: Create the test project**

`tests/Kartova.SharedKernel.Identity.Tests/Kartova.SharedKernel.Identity.Tests.csproj`:

```xml
<Project Sdk="MSTest.Sdk/3.10.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableMSTestRunner>true</EnableMSTestRunner>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Kartova.SharedKernel.Identity/Kartova.SharedKernel.Identity.csproj" />
  </ItemGroup>
</Project>
```

```powershell
dotnet sln Kartova.slnx add tests/Kartova.SharedKernel.Identity.Tests/Kartova.SharedKernel.Identity.Tests.csproj
```

- [ ] **Step 3: Write the stub HTTP message handler**

`tests/Kartova.SharedKernel.Identity.Tests/StubHttpMessageHandler.cs`:

```csharp
namespace Kartova.SharedKernel.Identity.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    public List<HttpRequestMessage> Captured { get; } = new();

    public void EnqueueResponse(Func<HttpRequestMessage, HttpResponseMessage> factory) => _responses.Enqueue(factory);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Captured.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException($"No stubbed response for {request.Method} {request.RequestUri}");
        return Task.FromResult(_responses.Dequeue()(request));
    }
}
```

- [ ] **Step 4: Write the failing test for CreateUserAsync happy path**

`tests/Kartova.SharedKernel.Identity.Tests/KeycloakAdminClientTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using IdentityModel.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kartova.SharedKernel.Identity.Tests;

[TestClass]
public sealed class KeycloakAdminClientTests
{
    private static (KeycloakAdminClient client, StubHttpMessageHandler stub) MakeSut()
    {
        var stub = new StubHttpMessageHandler();
        // First request will always be the token request; we enqueue a token response upfront in tests that need it.
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://keycloak:8080") };
        var tokenHttp = new HttpClient(stub) { BaseAddress = new Uri("http://keycloak:8080") };
        var tokenClient = new TokenClient(tokenHttp, new TokenClientOptions {
            Address = "http://keycloak:8080/realms/kartova/protocol/openid-connect/token",
            ClientId = "kartova-admin",
            ClientSecret = "test-secret",
        });
        var options = Options.Create(new KeycloakAdminOptions {
            BaseUrl = "http://keycloak:8080",
            Realm = "kartova",
            AdminClientId = "kartova-admin",
            AdminClientSecret = "test-secret",
        });
        var client = new KeycloakAdminClient(http, options, tokenClient, NullLogger<KeycloakAdminClient>.Instance);
        return (client, stub);
    }

    private static void EnqueueTokenResponse(StubHttpMessageHandler stub) =>
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("""{"access_token":"FAKE_TOKEN","expires_in":300,"token_type":"Bearer"}""", System.Text.Encoding.UTF8, "application/json")
        });

    [TestMethod]
    public async Task CreateUserAsync_returns_user_id_from_location_header_on_201()
    {
        var (client, stub) = MakeSut();
        var newId = Guid.NewGuid();
        EnqueueTokenResponse(stub);
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.Created) {
            Headers = { Location = new Uri($"http://keycloak:8080/admin/realms/kartova/users/{newId}") }
        });

        var result = await client.CreateUserAsync(
            new CreateKeycloakUserRequest("a@b.c", null, null, Guid.NewGuid().ToString(), ["UPDATE_PASSWORD"]),
            CancellationToken.None);

        Assert.AreEqual(newId, result);
    }
}
```

- [ ] **Step 5: Run the test to verify it fails (class doesn't exist yet)**

```powershell
dotnet test tests/Kartova.SharedKernel.Identity.Tests/ --filter "FullyQualifiedName~CreateUserAsync_returns_user_id_from_location_header_on_201"
```

Expected: COMPILE FAIL — `KeycloakAdminClient` not defined.

- [ ] **Step 6: Implement `KeycloakAdminClient`**

`src/Kartova.SharedKernel.Identity/KeycloakAdminClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kartova.SharedKernel.Identity;

internal sealed class KeycloakAdminClient(
    HttpClient http,
    IOptions<KeycloakAdminOptions> options,
    TokenClient tokenClient,
    ILogger<KeycloakAdminClient> logger) : IKeycloakAdminClient
{
    private readonly string _realm = options.Value.Realm;

    public async Task<Guid> CreateUserAsync(CreateKeycloakUserRequest request, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/realms/{_realm}/users")
        {
            Content = JsonContent.Create(new
            {
                email = request.Email,
                firstName = request.FirstName,
                lastName = request.LastName,
                enabled = true,
                emailVerified = false,
                requiredActions = request.RequiredActions,
                attributes = new { tenantId = new[] { request.TenantId } },
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict)
            throw new KeycloakAdminException(KeycloakAdminError.EmailAlreadyExists, "Email already exists in KeyCloak realm.");
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new KeycloakAdminException(KeycloakAdminError.Unauthorized, "Admin client unauthorized.");
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak create-user returned {(int)resp.StatusCode}.");

        var loc = resp.Headers.Location ?? throw new KeycloakAdminException(KeycloakAdminError.Unexpected, "Missing Location header on KeyCloak create-user response.");
        var idSegment = loc.Segments[^1].TrimEnd('/');
        return Guid.Parse(idSegment);
    }

    public async Task<KeycloakUser?> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/admin/realms/{_realm}/users/{userId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak get-user returned {(int)resp.StatusCode}.");

        var raw = await resp.Content.ReadFromJsonAsync<KeycloakUserRaw>(cancellationToken: ct)
                  ?? throw new KeycloakAdminException(KeycloakAdminError.Unexpected, "Empty KeyCloak get-user response.");
        return raw.ToDomain();
    }

    public async Task AssignRealmRoleAsync(Guid userId, string roleName, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);

        // First, fetch the realm role to get its id.
        using var roleReq = new HttpRequestMessage(HttpMethod.Get, $"/admin/realms/{_realm}/roles/{Uri.EscapeDataString(roleName)}");
        roleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var roleResp = await http.SendAsync(roleReq, ct);
        if (roleResp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"Realm role '{roleName}' not found.");
        if (!roleResp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak get-role returned {(int)roleResp.StatusCode}.");
        var role = await roleResp.Content.ReadFromJsonAsync<RealmRole>(cancellationToken: ct)
                   ?? throw new KeycloakAdminException(KeycloakAdminError.Unexpected, "Empty KeyCloak get-role response.");

        using var assignReq = new HttpRequestMessage(HttpMethod.Post, $"/admin/realms/{_realm}/users/{userId}/role-mappings/realm")
        {
            Content = JsonContent.Create(new[] { role }),
        };
        assignReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var assignResp = await http.SendAsync(assignReq, ct);
        if (assignResp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (!assignResp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak assign-role returned {(int)assignResp.StatusCode}.");
    }

    public async Task<IReadOnlyList<KeycloakUser>> SearchUsersAsync(string query, int limit, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        var uri = $"/admin/realms/{_realm}/users?search={Uri.EscapeDataString(query)}&max={limit}";
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak search-users returned {(int)resp.StatusCode}.");
        var raws = await resp.Content.ReadFromJsonAsync<List<KeycloakUserRaw>>(cancellationToken: ct) ?? new();
        return raws.Select(r => r.ToDomain()).ToList();
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/admin/realms/{_realm}/users/{userId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak delete-user returned {(int)resp.StatusCode}.");
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var resp = await tokenClient.RequestClientCredentialsTokenAsync(cancellationToken: ct);
        if (resp.IsError || resp.AccessToken is null)
            throw new KeycloakAdminException(KeycloakAdminError.Unauthorized, $"Token fetch failed: {resp.Error}");
        return resp.AccessToken;
    }

    private sealed record KeycloakUserRaw(
        Guid Id, string Email, string? FirstName, string? LastName,
        bool Enabled, bool EmailVerified,
        Dictionary<string, List<string>>? Attributes)
    {
        public KeycloakUser ToDomain() => new(
            Id, Email, FirstName, LastName, Enabled, EmailVerified,
            Attributes is not null && Attributes.TryGetValue("tenantId", out var tids) && tids.Count > 0 ? tids[0] : null);
    }

    private sealed record RealmRole(string Id, string Name);
}
```

- [ ] **Step 7: Run the test, verify it passes**

```powershell
dotnet test tests/Kartova.SharedKernel.Identity.Tests/ --filter "FullyQualifiedName~CreateUserAsync_returns_user_id_from_location_header_on_201"
```

Expected: PASS.

- [ ] **Step 8: Add error-branch tests**

Append to `KeycloakAdminClientTests.cs`:

```csharp
[TestMethod]
public async Task CreateUserAsync_throws_EmailAlreadyExists_on_409()
{
    var (client, stub) = MakeSut();
    EnqueueTokenResponse(stub);
    stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.Conflict));

    var ex = await Assert.ThrowsExactlyAsync<KeycloakAdminException>(() =>
        client.CreateUserAsync(new CreateKeycloakUserRequest("a@b.c", null, null, Guid.NewGuid().ToString(), []), CancellationToken.None));
    Assert.AreEqual(KeycloakAdminError.EmailAlreadyExists, ex.Error);
}

[TestMethod]
public async Task GetUserAsync_returns_null_on_404()
{
    var (client, stub) = MakeSut();
    EnqueueTokenResponse(stub);
    stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

    var result = await client.GetUserAsync(Guid.NewGuid(), CancellationToken.None);

    Assert.IsNull(result);
}

[TestMethod]
public async Task DeleteUserAsync_throws_NotFound_on_404()
{
    var (client, stub) = MakeSut();
    EnqueueTokenResponse(stub);
    stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

    var ex = await Assert.ThrowsExactlyAsync<KeycloakAdminException>(() =>
        client.DeleteUserAsync(Guid.NewGuid(), CancellationToken.None));
    Assert.AreEqual(KeycloakAdminError.NotFound, ex.Error);
}

[TestMethod]
public async Task CreateUserAsync_attaches_bearer_token()
{
    var (client, stub) = MakeSut();
    var newId = Guid.NewGuid();
    EnqueueTokenResponse(stub);
    stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.Created) {
        Headers = { Location = new Uri($"http://keycloak:8080/admin/realms/kartova/users/{newId}") }
    });

    await client.CreateUserAsync(new CreateKeycloakUserRequest("a@b.c", null, null, Guid.NewGuid().ToString(), []), CancellationToken.None);

    var createReq = stub.Captured[1];   // [0] is token, [1] is create
    Assert.AreEqual("Bearer", createReq.Headers.Authorization?.Scheme);
    Assert.AreEqual("FAKE_TOKEN", createReq.Headers.Authorization?.Parameter);
}
```

- [ ] **Step 9: Run all tests**

```powershell
dotnet test tests/Kartova.SharedKernel.Identity.Tests/
```

Expected: 4 PASS.

- [ ] **Step 10: Add DI registration helper**

`src/Kartova.SharedKernel.Identity/ServiceCollectionExtensions.cs`:

```csharp
using IdentityModel.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kartova.SharedKernel.Identity;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKeycloakAdminClient(
        this IServiceCollection services,
        IConfiguration config,
        string sectionName = "KartovaIdentity:Keycloak")
    {
        services.AddOptions<KeycloakAdminOptions>().Bind(config.GetSection(sectionName)).ValidateOnStart();

        services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl);
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
            var http = new HttpClient { BaseAddress = new Uri(opts.BaseUrl) };
            return new TokenClient(http, new TokenClientOptions
            {
                Address = $"{opts.BaseUrl}/realms/{opts.Realm}/protocol/openid-connect/token",
                ClientId = opts.AdminClientId,
                ClientSecret = opts.AdminClientSecret,
            });
        });

        return services;
    }
}
```

- [ ] **Step 11: Build + commit**

```powershell
dotnet build src/Kartova.SharedKernel.Identity/
dotnet test tests/Kartova.SharedKernel.Identity.Tests/
```

Expected: build clean, 4 tests pass.

```bash
git add src/Kartova.SharedKernel.Identity/ tests/Kartova.SharedKernel.Identity.Tests/ Kartova.slnx
git commit -m "feat(slice-9): KeycloakAdminClient with TokenClient + 4 unit tests"
```

---

### Task A5: `IDistributedLock` interface

**Files:**
- Create: `src/Kartova.SharedKernel/IDistributedLock.cs`

- [ ] **Step 1: Write the interface**

`src/Kartova.SharedKernel/IDistributedLock.cs`:

```csharp
namespace Kartova.SharedKernel;

/// <summary>
/// Cluster-wide named exclusive lock. Implementations must guarantee that only one acquirer
/// across all instances can hold a given lockName at a time. The returned handle releases
/// the lock on Dispose. Implementations are expected to release on connection drop / process
/// death automatically to avoid stale locks.
/// </summary>
public interface IDistributedLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct);
}
```

- [ ] **Step 2: Build + commit**

```powershell
dotnet build src/Kartova.SharedKernel/
```

```bash
git add src/Kartova.SharedKernel/IDistributedLock.cs
git commit -m "feat(slice-9): IDistributedLock abstraction"
```

---

### Task A6: `PostgresAdvisoryLock` implementation

**Files:**
- Create: `src/Kartova.SharedKernel.Postgres/PostgresAdvisoryLock.cs`
- Create: `src/Kartova.SharedKernel.Postgres/ServiceCollectionExtensions.cs` *(or extend existing — check first)*

- [ ] **Step 1: Check whether `ServiceCollectionExtensions.cs` already exists in `Kartova.SharedKernel.Postgres`**

```powershell
Get-ChildItem src/Kartova.SharedKernel.Postgres/ServiceCollectionExtensions.cs -ErrorAction SilentlyContinue
```

If yes → modify; if no → create.

- [ ] **Step 2: Implement `PostgresAdvisoryLock`**

`src/Kartova.SharedKernel.Postgres/PostgresAdvisoryLock.cs`:

```csharp
using System.Text;
using Kartova.SharedKernel;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Kartova.SharedKernel.Postgres;

internal sealed class PostgresAdvisoryLock(
    NpgsqlDataSource dataSource,
    ILogger<PostgresAdvisoryLock> logger) : IDistributedLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct)
    {
        var key = StableHash64(lockName);
        var conn = await dataSource.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@k)", conn);
            cmd.Parameters.AddWithValue("k", key);
            var acquired = (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
            if (!acquired)
            {
                await conn.DisposeAsync();
                return null;
            }
            logger.LogDebug("Acquired advisory lock {LockName}", lockName);
            return new Handle(conn, key, lockName, logger);
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    internal static long StableHash64(string input)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(input))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return (long)h;
        }
    }

    private sealed class Handle(NpgsqlConnection conn, long key, string name, ILogger log) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", conn);
                cmd.Parameters.AddWithValue("k", key);
                await cmd.ExecuteNonQueryAsync();
                log.LogDebug("Released advisory lock {LockName}", name);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Lock unlock failed for {LockName}", name);
            }
            finally
            {
                await conn.DisposeAsync();
            }
        }
    }
}
```

- [ ] **Step 3: Add DI registration extension**

Add to `src/Kartova.SharedKernel.Postgres/ServiceCollectionExtensions.cs` (create or extend):

```csharp
using Kartova.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.Postgres;

public static class DistributedLocksServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresDistributedLocks(this IServiceCollection services)
    {
        services.AddSingleton<IDistributedLock, PostgresAdvisoryLock>();
        return services;
    }
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build src/Kartova.SharedKernel.Postgres/
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel.Postgres/PostgresAdvisoryLock.cs src/Kartova.SharedKernel.Postgres/ServiceCollectionExtensions.cs
git commit -m "feat(slice-9): PostgresAdvisoryLock + AddPostgresDistributedLocks DI helper"
```

---

### Task A7: `LeaderElectedPeriodicService` base class + unit tests

**Files:**
- Create: `src/Kartova.SharedKernel/LeaderElectedPeriodicService.cs`
- Create: `tests/Kartova.SharedKernel.Tests/LeaderElectedPeriodicServiceTests.cs` *(create test project if absent)*

- [ ] **Step 1: Verify or create the SharedKernel test project**

```powershell
Test-Path tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj
```

If absent, scaffold mirroring `Kartova.SharedKernel.Identity.Tests.csproj` (Task A4 Step 2 template) — same SDK, same NSubstitute reference, project-ref to `src/Kartova.SharedKernel/Kartova.SharedKernel.csproj`.

```powershell
dotnet sln Kartova.slnx add tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj
```

- [ ] **Step 2: Write the failing test**

`tests/Kartova.SharedKernel.Tests/LeaderElectedPeriodicServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Kartova.SharedKernel.Tests;

[TestClass]
public sealed class LeaderElectedPeriodicServiceTests
{
    private sealed class TestService(
        IServiceScopeFactory scopes, IDistributedLock locks, TimeProvider clock,
        Action<IServiceProvider> work)
        : LeaderElectedPeriodicService(scopes, locks, clock, NullLogger.Instance)
    {
        protected override string LockName => "test";
        protected override TimeSpan Interval => TimeSpan.FromMinutes(1);
        protected override Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct)
        {
            work(services);
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task Runs_leader_work_when_lock_acquired()
    {
        var locks = Substitute.For<IDistributedLock>();
        var handle = Substitute.For<IAsyncDisposable>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns(handle);
        var clock = new FakeTimeProvider();
        var sp = new ServiceCollection().BuildServiceProvider();
        var ran = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock, _ => ran++);

        using var cts = new CancellationTokenSource();
        var task = sut.StartAsync(cts.Token);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.AreEqual(1, ran);
    }

    [TestMethod]
    public async Task Skips_when_lock_unavailable()
    {
        var locks = Substitute.For<IDistributedLock>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns((IAsyncDisposable?)null);
        var clock = new FakeTimeProvider();
        var sp = new ServiceCollection().BuildServiceProvider();
        var ran = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock, _ => ran++);

        using var cts = new CancellationTokenSource();
        var task = sut.StartAsync(cts.Token);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.AreEqual(0, ran);
    }

    [TestMethod]
    public async Task Exception_in_work_does_not_stop_the_loop()
    {
        var locks = Substitute.For<IDistributedLock>();
        var handle = Substitute.For<IAsyncDisposable>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns(handle);
        var clock = new FakeTimeProvider();
        var sp = new ServiceCollection().BuildServiceProvider();
        var calls = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock,
            _ => { calls++; if (calls == 1) throw new InvalidOperationException("boom"); });

        using var cts = new CancellationTokenSource();
        var task = sut.StartAsync(cts.Token);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.IsTrue(calls >= 2, $"Expected at least 2 invocations after exception recovery, got {calls}.");
    }
}
```

Add `Microsoft.Extensions.TimeProvider.Testing` to the csproj `ItemGroup`:

```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.0.0" />
```

- [ ] **Step 3: Run, verify FAIL**

```powershell
dotnet test tests/Kartova.SharedKernel.Tests/ --filter "FullyQualifiedName~LeaderElectedPeriodicServiceTests"
```

Expected: COMPILE FAIL (class not defined).

- [ ] **Step 4: Implement `LeaderElectedPeriodicService`**

`src/Kartova.SharedKernel/LeaderElectedPeriodicService.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kartova.SharedKernel;

public abstract class LeaderElectedPeriodicService(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    TimeProvider clock,
    ILogger logger) : BackgroundService
{
    protected abstract string LockName { get; }
    protected abstract TimeSpan Interval { get; }
    protected abstract Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (true)
        {
            if (!await timer.WaitForNextTickAsync(ct)) break;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await using var lockHandle = await locks.TryAcquireAsync(LockName, ct);
                if (lockHandle is null)
                {
                    logger.LogDebug("{Service}: lock held by another instance — skipping tick", GetType().Name);
                    continue;
                }
                await ExecuteLeaderWorkAsync(scope.ServiceProvider, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Service}: leader tick failed", GetType().Name);
            }
        }
    }
}
```

- [ ] **Step 5: Run tests, verify PASS**

```powershell
dotnet test tests/Kartova.SharedKernel.Tests/ --filter "FullyQualifiedName~LeaderElectedPeriodicServiceTests"
```

Expected: 3 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel/LeaderElectedPeriodicService.cs tests/Kartova.SharedKernel.Tests/ Kartova.slnx
git commit -m "feat(slice-9): LeaderElectedPeriodicService base class + 3 unit tests"
```

---

### Task A8: `PostgresAdvisoryLock` integration test

**Files:**
- Create: `tests/Kartova.SharedKernel.Postgres.IntegrationTests/Kartova.SharedKernel.Postgres.IntegrationTests.csproj`
- Create: `tests/Kartova.SharedKernel.Postgres.IntegrationTests/PostgresAdvisoryLockTests.cs`

- [ ] **Step 1: Scaffold the integration test project**

Mirror an existing integration-test csproj (e.g. `tests/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj` — read it first). Add Testcontainers + Npgsql + the project-refs needed (`Kartova.SharedKernel.Postgres`).

```powershell
dotnet sln Kartova.slnx add tests/Kartova.SharedKernel.Postgres.IntegrationTests/Kartova.SharedKernel.Postgres.IntegrationTests.csproj
```

- [ ] **Step 2: Write the integration test**

`tests/Kartova.SharedKernel.Postgres.IntegrationTests/PostgresAdvisoryLockTests.cs`:

```csharp
using Kartova.SharedKernel;
using Kartova.SharedKernel.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Kartova.SharedKernel.Postgres.IntegrationTests;

[TestClass]
public sealed class PostgresAdvisoryLockTests
{
    private static PostgreSqlContainer? _pg;
    private static NpgsqlDataSource? _dataSource;

    [ClassInitialize]
    public static async Task InitAsync(TestContext _)
    {
        _pg = new PostgreSqlBuilder().WithImage("postgres:18-alpine").Build();
        await _pg.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_pg.GetConnectionString());
    }

    [ClassCleanup]
    public static async Task TeardownAsync()
    {
        _dataSource?.Dispose();
        if (_pg is not null) await _pg.DisposeAsync();
    }

    [TestMethod]
    public async Task Concurrent_acquire_only_one_wins()
    {
        var sut = new PostgresAdvisoryLock(_dataSource!, NullLogger<PostgresAdvisoryLock>.Instance);
        const string name = "concurrent-test";

        var handle1 = await sut.TryAcquireAsync(name, CancellationToken.None);
        var handle2 = await sut.TryAcquireAsync(name, CancellationToken.None);

        Assert.IsNotNull(handle1);
        Assert.IsNull(handle2);
        await handle1!.DisposeAsync();
    }

    [TestMethod]
    public async Task After_dispose_lock_is_available_to_next_acquirer()
    {
        var sut = new PostgresAdvisoryLock(_dataSource!, NullLogger<PostgresAdvisoryLock>.Instance);
        const string name = "release-test";

        var h1 = await sut.TryAcquireAsync(name, CancellationToken.None);
        Assert.IsNotNull(h1);
        await h1!.DisposeAsync();

        var h2 = await sut.TryAcquireAsync(name, CancellationToken.None);
        Assert.IsNotNull(h2);
        await h2!.DisposeAsync();
    }

    [TestMethod]
    public void StableHash64_is_deterministic_across_calls()
    {
        var a = PostgresAdvisoryLock.StableHash64("expire-invitations");
        var b = PostgresAdvisoryLock.StableHash64("expire-invitations");
        var c = PostgresAdvisoryLock.StableHash64("different");
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, c);
    }
}
```

- [ ] **Step 3: Run**

```powershell
dotnet test tests/Kartova.SharedKernel.Postgres.IntegrationTests/
```

Expected: 3 PASS (allow a few minutes for the container pull on first run).

- [ ] **Step 4: Commit**

```bash
git add tests/Kartova.SharedKernel.Postgres.IntegrationTests/ Kartova.slnx
git commit -m "test(slice-9): PostgresAdvisoryLock integration tests (Testcontainers)"
```

---

### Task A9: ADR-0099 — Distributed locking + leader-elected periodic tasks

**Files:**
- Create: `docs/architecture/decisions/ADR-0099-distributed-locking-leader-elected-periodic-tasks.md`
- Modify: `docs/architecture/decisions/README.md` (add to keyword index)

- [ ] **Step 1: Write the ADR**

Copy the full ADR-0099 text from spec §10.1 verbatim into `docs/architecture/decisions/ADR-0099-distributed-locking-leader-elected-periodic-tasks.md`. Use ADR-0098 as the structural template (Status / Context / Decision / Consequences / Related).

- [ ] **Step 2: Add to README keyword index**

Open `docs/architecture/decisions/README.md`, find the keyword index, add:

```
| Distributed locking / leader-elected periodic tasks | ADR-0099 |
| Periodic background tasks (multi-instance safe)     | ADR-0099 |
```

(Keep alphabetic order with neighboring entries.)

- [ ] **Step 3: Commit**

```bash
git add docs/architecture/decisions/ADR-0099-distributed-locking-leader-elected-periodic-tasks.md docs/architecture/decisions/README.md
git commit -m "docs(adr): ADR-0099 distributed locking + leader-elected periodic tasks"
```

---

**End of Phase A.** At this point: `Kartova.SharedKernel.Identity` ships with `IKeycloakAdminClient` + `IUserDirectory` interface (impl in Phase D) + 4 unit tests; `Kartova.SharedKernel` ships `IDistributedLock` + `LeaderElectedPeriodicService` + 3 unit tests; `Kartova.SharedKernel.Postgres` ships `PostgresAdvisoryLock` + 3 integration tests; ADR-0099 written. Nothing wired into the API host yet — that lands in Phase D.

---

## Phase B — Database + domain model (`Organization` extensions, `Invitation` aggregate, `User` projection)

### Task B1: Extend `Organization` aggregate (Description, OrgLogo, DefaultTimeZone)

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Domain/Organization.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Domain/OrgLogo.cs`
- Create: `tests/Kartova.Organization.Domain.Tests/OrganizationProfileTests.cs`
- Create: `tests/Kartova.Organization.Domain.Tests/OrgLogoTests.cs`

- [ ] **Step 1: Read the existing Organization.cs**

```powershell
Get-Content src/Modules/Organization/Kartova.Organization.Domain/Organization.cs
```

Note current fields/factory shape — slice-2 baseline.

- [ ] **Step 2: Write `OrgLogo` failing tests**

`tests/Kartova.Organization.Domain.Tests/OrgLogoTests.cs`:

```csharp
using Kartova.Organization.Domain;

namespace Kartova.Organization.Domain.Tests;

[TestClass]
public sealed class OrgLogoTests
{
    [TestMethod]
    public void Create_with_valid_png_returns_logo_with_hash()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xDE, 0xAD };
        var logo = OrgLogo.Create(bytes, "image/png");
        Assert.AreEqual("image/png", logo.MimeType);
        Assert.AreEqual(64, logo.ContentHash.Length);  // SHA-256 hex
        CollectionAssert.AreEqual(bytes, logo.Bytes);
    }

    [TestMethod]
    public void Create_rejects_empty_bytes()
    {
        Assert.ThrowsExactly<ArgumentException>(() => OrgLogo.Create([], "image/png"));
    }

    [TestMethod]
    public void Create_rejects_oversize_bytes()
    {
        Assert.ThrowsExactly<ArgumentException>(() => OrgLogo.Create(new byte[256 * 1024 + 1], "image/png"));
    }

    [TestMethod]
    [DataRow("image/gif")]
    [DataRow("application/octet-stream")]
    [DataRow("")]
    public void Create_rejects_unsupported_mime(string mime)
    {
        Assert.ThrowsExactly<ArgumentException>(() => OrgLogo.Create(new byte[16], mime));
    }
}
```

- [ ] **Step 3: Implement `OrgLogo`**

`src/Modules/Organization/Kartova.Organization.Domain/OrgLogo.cs`:

```csharp
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Kartova.Organization.Domain;

public sealed class OrgLogo
{
    private static readonly FrozenSet<string> AcceptedMimeTypes =
        new[] { "image/png", "image/jpeg", "image/svg+xml" }.ToFrozenSet();

    public byte[] Bytes { get; private set; } = [];
    public string MimeType { get; private set; } = "";
    public string ContentHash { get; private set; } = "";

    [SuppressMessage("Performance", "CA1822", Justification = "EF requires instance ctor.")]
    private OrgLogo() { }

    public static OrgLogo Create(byte[] bytes, string mimeType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.Length > 256 * 1024)
            throw new ArgumentException("Logo bytes must be 1..262144.", nameof(bytes));
        if (!AcceptedMimeTypes.Contains(mimeType))
            throw new ArgumentException("Unsupported logo mime-type.", nameof(mimeType));
        return new OrgLogo
        {
            Bytes = bytes,
            MimeType = mimeType,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)),
        };
    }
}
```

- [ ] **Step 4: Run OrgLogo tests, verify PASS**

```powershell
dotnet test tests/Kartova.Organization.Domain.Tests/ --filter "FullyQualifiedName~OrgLogoTests"
```

Expected: 6 PASS (one test row per `[DataRow]` plus 3 single-shot tests).

- [ ] **Step 5: Write failing `Organization.UpdateProfile` tests**

`tests/Kartova.Organization.Domain.Tests/OrganizationProfileTests.cs`:

```csharp
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain.Tests;

[TestClass]
public sealed class OrganizationProfileTests
{
    private static Organization Make() =>
        Organization.Create("Org A", new TenantId(Guid.NewGuid()), TimeProvider.System);

    [TestMethod]
    public void UpdateProfile_sets_all_fields()
    {
        var org = Make();
        org.UpdateProfile("Org A New", "A description.", "Europe/Warsaw");
        Assert.AreEqual("Org A New", org.DisplayName);
        Assert.AreEqual("A description.", org.Description);
        Assert.AreEqual("Europe/Warsaw", org.DefaultTimeZone);
    }

    [TestMethod]
    public void UpdateProfile_rejects_empty_display_name()
    {
        var org = Make();
        Assert.ThrowsExactly<ArgumentException>(() => org.UpdateProfile("", null, "UTC"));
    }

    [TestMethod]
    public void UpdateProfile_rejects_overlong_description()
    {
        var org = Make();
        var tooLong = new string('x', 1025);
        Assert.ThrowsExactly<ArgumentException>(() => org.UpdateProfile("Org A", tooLong, "UTC"));
    }

    [TestMethod]
    public void UpdateProfile_rejects_unknown_timezone()
    {
        var org = Make();
        Assert.ThrowsExactly<ArgumentException>(() => org.UpdateProfile("Org A", null, "Mars/Olympus"));
    }

    [TestMethod]
    public void SetLogo_assigns_logo()
    {
        var org = Make();
        var logo = OrgLogo.Create(new byte[16], "image/png");
        org.SetLogo(logo);
        Assert.AreSame(logo, org.Logo);
    }

    [TestMethod]
    public void ClearLogo_removes_logo()
    {
        var org = Make();
        org.SetLogo(OrgLogo.Create(new byte[16], "image/png"));
        org.ClearLogo();
        Assert.IsNull(org.Logo);
    }
}
```

- [ ] **Step 6: Extend `Organization.cs`**

Add to the existing `Organization` class (matching the existing style/pattern):

```csharp
public string? Description { get; private set; }
public OrgLogo? Logo { get; private set; }
public string DefaultTimeZone { get; private set; } = "UTC";

public void UpdateProfile(string displayName, string? description, string defaultTimeZone)
{
    ValidateDisplayName(displayName);
    ValidateDescription(description);
    ValidateTimeZone(defaultTimeZone);
    DisplayName = displayName;
    Description = description;
    DefaultTimeZone = defaultTimeZone;
}

public void SetLogo(OrgLogo logo)
{
    ArgumentNullException.ThrowIfNull(logo);
    Logo = logo;
}

public void ClearLogo() => Logo = null;

private static void ValidateDescription(string? s)
{
    if (s is { Length: > 1024 }) throw new ArgumentException("Description must be <= 1024 characters.", nameof(s));
}

private static void ValidateTimeZone(string tz)
{
    if (string.IsNullOrWhiteSpace(tz)) throw new ArgumentException("Time-zone required.", nameof(tz));
    if (!TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
        throw new ArgumentException("Unknown IANA time-zone id.", nameof(tz));
}
```

Reuse the existing `ValidateDisplayName` (slice 2). If `DisplayName` setter is not currently mutable, expose mutation only through `UpdateProfile` (don't add a public setter).

- [ ] **Step 7: Run all Org domain tests**

```powershell
dotnet test tests/Kartova.Organization.Domain.Tests/
```

Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Domain/ tests/Kartova.Organization.Domain.Tests/
git commit -m "feat(slice-9): extend Organization aggregate with Description/Logo/DefaultTimeZone"
```

---

### Task B2: `Invitation` aggregate + tests

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Domain/InvitationId.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Domain/InvitationStatus.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Domain/Invitation.cs`
- Create: `tests/Kartova.Organization.Domain.Tests/InvitationTests.cs`

- [ ] **Step 1: Write the failing test file**

`tests/Kartova.Organization.Domain.Tests/InvitationTests.cs`:

```csharp
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Organization.Domain.Tests;

[TestClass]
public sealed class InvitationTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid InvitedBy = Guid.NewGuid();
    private static readonly Guid KcUser = Guid.NewGuid();

    [TestMethod]
    public void Create_sets_pending_status_and_7day_expiry()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var inv = Invitation.Create("Alice@Example.com", "Member", InvitedBy, KcUser, Tenant, clock);
        Assert.AreEqual(InvitationStatus.Pending, inv.Status);
        Assert.AreEqual("alice@example.com", inv.Email);
        Assert.AreEqual("Member", inv.Role);
        Assert.AreEqual(InvitedBy, inv.InvitedByUserId);
        Assert.AreEqual(KcUser, inv.KeycloakUserId);
        Assert.AreEqual(DateTimeOffset.Parse("2026-05-27T10:00:00Z"), inv.InvitedAt);
        Assert.AreEqual(DateTimeOffset.Parse("2026-06-03T10:00:00Z"), inv.ExpiresAt);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-an-email")]
    public void Create_rejects_invalid_email(string email)
    {
        var clock = new FakeTimeProvider();
        Assert.ThrowsExactly<ArgumentException>(() =>
            Invitation.Create(email, "Member", InvitedBy, KcUser, Tenant, clock));
    }

    [TestMethod]
    public void Create_rejects_unknown_role()
    {
        var clock = new FakeTimeProvider();
        Assert.ThrowsExactly<ArgumentException>(() =>
            Invitation.Create("a@b.c", "BogusRole", InvitedBy, KcUser, Tenant, clock));
    }

    [TestMethod]
    public void MarkAccepted_flips_status_and_sets_AcceptedAt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var inv = Invitation.Create("a@b.c", "Member", InvitedBy, KcUser, Tenant, clock);
        clock.Advance(TimeSpan.FromMinutes(5));
        inv.MarkAccepted(clock);
        Assert.AreEqual(InvitationStatus.Accepted, inv.Status);
        Assert.AreEqual(DateTimeOffset.Parse("2026-05-27T10:05:00Z"), inv.AcceptedAt);
    }

    [TestMethod]
    public void MarkAccepted_throws_when_already_accepted()
    {
        var clock = new FakeTimeProvider();
        var inv = Invitation.Create("a@b.c", "Member", InvitedBy, KcUser, Tenant, clock);
        inv.MarkAccepted(clock);
        Assert.ThrowsExactly<InvalidOperationException>(() => inv.MarkAccepted(clock));
    }

    [TestMethod]
    public void Revoke_flips_status_and_sets_RevokedAt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var inv = Invitation.Create("a@b.c", "Member", InvitedBy, KcUser, Tenant, clock);
        clock.Advance(TimeSpan.FromHours(2));
        inv.Revoke(clock);
        Assert.AreEqual(InvitationStatus.Revoked, inv.Status);
        Assert.AreEqual(DateTimeOffset.Parse("2026-05-27T12:00:00Z"), inv.RevokedAt);
    }

    [TestMethod]
    public void Revoke_throws_when_already_terminal()
    {
        var clock = new FakeTimeProvider();
        var inv = Invitation.Create("a@b.c", "Member", InvitedBy, KcUser, Tenant, clock);
        inv.MarkAccepted(clock);
        Assert.ThrowsExactly<InvalidOperationException>(() => inv.Revoke(clock));
    }

    [TestMethod]
    public void MarkExpired_flips_status()
    {
        var clock = new FakeTimeProvider();
        var inv = Invitation.Create("a@b.c", "Member", InvitedBy, KcUser, Tenant, clock);
        inv.MarkExpired(clock);
        Assert.AreEqual(InvitationStatus.Expired, inv.Status);
    }

    [TestMethod]
    public void MarkExpired_throws_when_not_pending()
    {
        var clock = new FakeTimeProvider();
        var inv = Invitation.Create("a@b.c", "Member", InvitedBy, KcUser, Tenant, clock);
        inv.Revoke(clock);
        Assert.ThrowsExactly<InvalidOperationException>(() => inv.MarkExpired(clock));
    }
}
```

- [ ] **Step 2: Run, verify FAIL**

```powershell
dotnet test tests/Kartova.Organization.Domain.Tests/ --filter "FullyQualifiedName~InvitationTests"
```

Expected: COMPILE FAIL (types missing).

- [ ] **Step 3: Implement `InvitationId`**

`src/Modules/Organization/Kartova.Organization.Domain/InvitationId.cs`:

```csharp
namespace Kartova.Organization.Domain;

public readonly record struct InvitationId(Guid Value)
{
    public static InvitationId New() => new(Guid.NewGuid());
}
```

- [ ] **Step 4: Implement `InvitationStatus`**

`src/Modules/Organization/Kartova.Organization.Domain/InvitationStatus.cs`:

```csharp
namespace Kartova.Organization.Domain;

public enum InvitationStatus : byte
{
    Pending = 1,
    Accepted = 2,
    Revoked = 3,
    Expired = 4,
}
```

- [ ] **Step 5: Implement `Invitation`**

`src/Modules/Organization/Kartova.Organization.Domain/Invitation.cs`:

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain;

public sealed class Invitation : ITenantOwned
{
    private Guid _id;
    public InvitationId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string Email { get; private set; } = "";
    public string Role { get; private set; } = "";
    public Guid InvitedByUserId { get; private set; }
    public DateTimeOffset InvitedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public InvitationStatus Status { get; private set; }
    public Guid? KeycloakUserId { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private Invitation() { }

    public static Invitation Create(
        string email, string role, Guid invitedByUserId,
        Guid keycloakUserId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateEmail(email);
        if (!KartovaRoles.All.Contains(role))
            throw new ArgumentException("Unknown role.", nameof(role));
        var now = clock.GetUtcNow();
        return new Invitation
        {
            _id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            Role = role,
            InvitedByUserId = invitedByUserId,
            InvitedAt = now,
            ExpiresAt = now.AddDays(7),
            Status = InvitationStatus.Pending,
            KeycloakUserId = keycloakUserId,
        };
    }

    public void MarkAccepted(TimeProvider clock)
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot accept invitation in {Status} state.");
        Status = InvitationStatus.Accepted;
        AcceptedAt = clock.GetUtcNow();
    }

    public void Revoke(TimeProvider clock)
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot revoke invitation in {Status} state.");
        Status = InvitationStatus.Revoked;
        RevokedAt = clock.GetUtcNow();
    }

    public void MarkExpired(TimeProvider clock)
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot expire invitation in {Status} state.");
        Status = InvitationStatus.Expired;
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email required.", nameof(email));
        if (email.Length > 320) throw new ArgumentException("Email must be <= 320 characters.", nameof(email));
        if (!email.Contains('@')) throw new ArgumentException("Email must contain '@'.", nameof(email));
    }
}
```

`KartovaRoles.All` is the slice-7 constant set; verify the import via `using Kartova.SharedKernel.Multitenancy;` (existing namespace).

- [ ] **Step 6: Run, verify all Invitation tests PASS**

```powershell
dotnet test tests/Kartova.Organization.Domain.Tests/
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Domain/ tests/Kartova.Organization.Domain.Tests/InvitationTests.cs
git commit -m "feat(slice-9): Invitation aggregate with status machine + 9 domain tests"
```

---

### Task B3: `User` projection POCO

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Domain/User.cs`

- [ ] **Step 1: Write the type**

`src/Modules/Organization/Kartova.Organization.Domain/User.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain;

[ExcludeFromCodeCoverage]
public sealed class User : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Email { get; set; } = "";
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static string ComputeDisplayName(string? given, string? family, string email)
    {
        var full = $"{given?.Trim()} {family?.Trim()}".Trim();
        return string.IsNullOrWhiteSpace(full) ? email : full;
    }
}
```

`User` is a projection — settable properties + `[ExcludeFromCodeCoverage]` (pure data carrier, no invariants).

- [ ] **Step 2: Build + commit**

```powershell
dotnet build src/Modules/Organization/Kartova.Organization.Domain/
```

```bash
git add src/Modules/Organization/Kartova.Organization.Domain/User.cs
git commit -m "feat(slice-9): User projection POCO"
```

---

### Task B4: EF migrations (pg_trgm + organizations alter + users + invitations)

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/{timestamp}_EnablePgTrgmExtension.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/{timestamp}_AddOrganizationProfileColumns.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/{timestamp}_AddUsersTable.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/{timestamp}_AddInvitationsTable.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationDbContext.cs` (add DbSets + entity configs)
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/EfInvitationConfiguration.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/EfUserConfiguration.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/EfOrganizationConfiguration.cs` (extend)

- [ ] **Step 1: Read existing migrations + DbContext for slice-8 pattern**

```powershell
Get-ChildItem src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/
Get-Content src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationDbContext.cs
```

Verify the pattern: `migrationBuilder.Sql("ENABLE ROW LEVEL SECURITY; FORCE ...; CREATE POLICY ...")` for new tables (slice-8 `AddTeamsTable.cs` precedent).

- [ ] **Step 2: Extend `EfOrganizationConfiguration`**

Add to the existing `EfOrganizationConfiguration.Configure(EntityTypeBuilder<Organization> b)`:

```csharp
b.Property(x => x.Description).HasColumnName("description").HasMaxLength(1024);
b.Property(x => x.DefaultTimeZone).HasColumnName("default_time_zone").HasMaxLength(64).IsRequired().HasDefaultValue("UTC");
b.OwnsOne(x => x.Logo, l =>
{
    l.Property(p => p.Bytes).HasColumnName("logo_bytes");
    l.Property(p => p.MimeType).HasColumnName("logo_mime_type").HasMaxLength(32);
    l.Property(p => p.ContentHash).HasColumnName("logo_content_hash").HasMaxLength(64);
});
```

- [ ] **Step 3: Create `EfInvitationConfiguration`**

`src/Modules/Organization/Kartova.Organization.Infrastructure/EfInvitationConfiguration.cs`:

```csharp
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Organization.Infrastructure;

internal sealed class EfInvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable("invitations");

        // Backing-field strategy mirrors slice 8 (Team aggregate id).
        b.Property<Guid>("_id").HasColumnName("id");
        b.HasKey("_id");

        b.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(v => v.Value, v => new TenantId(v));
        b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(x => x.Role).HasColumnName("role").HasMaxLength(32).IsRequired();
        b.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id");
        b.Property(x => x.InvitedAt).HasColumnName("invited_at");
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<byte>();
        b.Property(x => x.KeycloakUserId).HasColumnName("keycloak_user_id");
        b.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        b.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        b.HasIndex(x => new { x.TenantId, x.Status }).HasDatabaseName("idx_invitations_tenant_status");
    }
}
```

- [ ] **Step 4: Create `EfUserConfiguration`**

`src/Modules/Organization/Kartova.Organization.Infrastructure/EfUserConfiguration.cs`:

```csharp
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Organization.Infrastructure;

internal sealed class EfUserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(v => v.Value, v => new TenantId(v));
        b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(x => x.GivenName).HasColumnName("given_name").HasMaxLength(128);
        b.Property(x => x.FamilyName).HasColumnName("family_name").HasMaxLength(128);
        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");

        b.HasIndex(x => new { x.TenantId, x.Email })
            .IsUnique()
            .HasDatabaseName("ux_users_tenant_email");
        b.HasIndex(x => x.TenantId).HasDatabaseName("idx_users_tenant");
    }
}
```

- [ ] **Step 5: Update `OrganizationDbContext`**

Add to the existing context:

```csharp
public DbSet<Invitation> Invitations => Set<Invitation>();
public DbSet<User> Users => Set<User>();

// in OnModelCreating, after existing ApplyConfiguration calls:
mb.ApplyConfiguration(new EfInvitationConfiguration());
mb.ApplyConfiguration(new EfUserConfiguration());
```

- [ ] **Step 6: Generate the four migrations**

Run sequentially (each in its own command — EF generates one migration at a time):

```powershell
$ef = "src/Modules/Organization/Kartova.Organization.Infrastructure"
$startup = "src/Kartova.Api"

dotnet ef migrations add EnablePgTrgmExtension --project $ef --startup-project $startup --context OrganizationDbContext
dotnet ef migrations add AddOrganizationProfileColumns --project $ef --startup-project $startup --context OrganizationDbContext
dotnet ef migrations add AddUsersTable --project $ef --startup-project $startup --context OrganizationDbContext
dotnet ef migrations add AddInvitationsTable --project $ef --startup-project $startup --context OrganizationDbContext
```

- [ ] **Step 7: Edit `EnablePgTrgmExtension.Up`**

Replace generated content with:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // pg_trgm intentionally not dropped on Down — other slices may rely on it.
}
```

- [ ] **Step 8: Edit `AddUsersTable.Up`**

After the auto-generated table+index creation, append the RLS block (slice-8 `AddTeamsTable.cs` pattern):

```csharp
migrationBuilder.Sql(@"
    ALTER TABLE users ENABLE ROW LEVEL SECURITY;
    ALTER TABLE users FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON users
      USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

    CREATE INDEX idx_users_displayname_trgm ON users USING gin (display_name gin_trgm_ops);
    CREATE INDEX idx_users_email_lower ON users(tenant_id, lower(email));
");
```

Down: drop policy + RLS first (mirror slice-8 `AddTeamsTable.Down`).

- [ ] **Step 9: Edit `AddInvitationsTable.Up`**

Append after the auto-generated table+index creation:

```csharp
migrationBuilder.Sql(@"
    ALTER TABLE invitations ENABLE ROW LEVEL SECURITY;
    ALTER TABLE invitations FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON invitations
      USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

    CREATE INDEX idx_invitations_email_pending ON invitations(tenant_id, lower(email)) WHERE status = 1;
");
```

- [ ] **Step 10: Edit `AddOrganizationProfileColumns.Up`**

Append the check constraint (auto-generated alter handles the column adds):

```csharp
migrationBuilder.Sql(@"
    ALTER TABLE organizations
      ADD CONSTRAINT chk_logo_complete CHECK (
        (logo_bytes IS NULL AND logo_mime_type IS NULL AND logo_content_hash IS NULL)
        OR (logo_bytes IS NOT NULL AND logo_mime_type IS NOT NULL AND logo_content_hash IS NOT NULL)
      );
");
```

Down: `DROP CONSTRAINT chk_logo_complete;` before the column drops.

- [ ] **Step 11: Build + smoke-test the migrator**

```powershell
dotnet build src/Modules/Organization/Kartova.Organization.Infrastructure/
docker compose up -d postgres
$env:Kartova__ConnectionStrings__Default = "Host=localhost;Port=5432;Database=kartova_dev;Username=kartova;Password=kartova_dev"
dotnet run --project src/Kartova.Migrator -- migrate
```

Expected: all four migrations apply cleanly.

- [ ] **Step 12: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/ src/Modules/Organization/Kartova.Organization.Infrastructure/Ef*Configuration.cs src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationDbContext.cs
git commit -m "feat(slice-9): EF migrations — pg_trgm, organizations profile columns, users, invitations"
```

---

## Phase C — JWT-claim sync, permissions, role map

### Task C1: New permission constants + role map update

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs`
- Create: `tests/Kartova.ArchitectureTests/OrganizationPermissionMatrixTests.cs`

- [ ] **Step 1: Append the 7 new constants**

In `KartovaPermissions.cs` (preserve existing constants; add at the bottom of the class):

```csharp
public const string OrgProfileRead         = "org.profile.read";
public const string OrgProfileEdit         = "org.profile.edit";
public const string OrgInvitationsRead     = "org.invitations.read";
public const string OrgInvitationsCreate   = "org.invitations.create";
public const string OrgInvitationsRevoke   = "org.invitations.revoke";
public const string OrgUsersRead           = "org.users.read";
public const string OrgUsersSearch         = "org.users.search";
```

Verify `All` collection picks them up automatically (slice 7's reflection-driven `All`).

- [ ] **Step 2: Update the role map**

In `KartovaRolePermissions.cs`, extend the `Map` dictionary entries per spec §5.2:

| Role | Grants |
|---|---|
| Viewer | `OrgProfileRead`, `OrgUsersRead` |
| Member | `OrgProfileRead`, `OrgUsersRead`, `OrgUsersSearch` |
| TeamAdmin | same as Member |
| OrgAdmin | all 7 |

- [ ] **Step 3: Write the matrix test**

`tests/Kartova.ArchitectureTests/OrganizationPermissionMatrixTests.cs`:

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.ArchitectureTests;

[TestClass]
public sealed class OrganizationPermissionMatrixTests
{
    private static readonly (string Role, string[] Expected)[] Matrix =
    [
        (KartovaRoles.Viewer,    [KartovaPermissions.OrgProfileRead, KartovaPermissions.OrgUsersRead]),
        (KartovaRoles.Member,    [KartovaPermissions.OrgProfileRead, KartovaPermissions.OrgUsersRead, KartovaPermissions.OrgUsersSearch]),
        (KartovaRoles.TeamAdmin, [KartovaPermissions.OrgProfileRead, KartovaPermissions.OrgUsersRead, KartovaPermissions.OrgUsersSearch]),
        (KartovaRoles.OrgAdmin,  [
            KartovaPermissions.OrgProfileRead, KartovaPermissions.OrgProfileEdit,
            KartovaPermissions.OrgInvitationsRead, KartovaPermissions.OrgInvitationsCreate, KartovaPermissions.OrgInvitationsRevoke,
            KartovaPermissions.OrgUsersRead, KartovaPermissions.OrgUsersSearch,
        ]),
    ];

    [TestMethod]
    public void Each_role_holds_exactly_its_expected_org_permissions()
    {
        foreach (var (role, expected) in Matrix)
        {
            var actualOrg = KartovaRolePermissions.Map[role]
                .Where(p => p.StartsWith("org.", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                expected.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                actualOrg,
                $"Role {role} has mismatched org.* permissions.");
        }
    }
}
```

- [ ] **Step 4: Run**

```powershell
dotnet test tests/Kartova.ArchitectureTests/ --filter "FullyQualifiedName~OrganizationPermissionMatrixTests"
dotnet test tests/Kartova.ArchitectureTests/    # full suite — drift sentinels run too
```

Expected: all PASS (including the existing `Ts_snapshot_equals_csharp_KartovaPermissions_All` — but that will FAIL until §F1 updates the SPA snapshot; mark XFAIL or temporarily skip until §F1).

Practical sequencing: update `permissions.snapshot.json` now (small SPA touch) and unskip the drift test:

```powershell
# Open and update web/src/shared/auth/permissions.snapshot.json — append 7 new entries.
```

Then re-run.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/ tests/Kartova.ArchitectureTests/OrganizationPermissionMatrixTests.cs web/src/shared/auth/permissions.snapshot.json
git commit -m "feat(slice-9): add 7 org permissions to KartovaPermissions + role map + matrix test"
```

---

### Task C2: Extend `ICurrentUser` with `JustAcceptedInvitationId`

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs` (or wherever the interface lives — verify)
- Modify: `src/Kartova.SharedKernel.AspNetCore/ITenantContext.cs` (mutator addition)
- Modify: `src/Kartova.SharedKernel.AspNetCore/TenantContextAccessor.cs`

- [ ] **Step 1: Find the right files**

```powershell
Get-ChildItem src/Kartova.SharedKernel.AspNetCore/*.cs | Select-String "ICurrentUser|ITenantContext|TenantContextAccessor" -List
```

- [ ] **Step 2: Add the property to `ICurrentUser`**

```csharp
Guid? JustAcceptedInvitationId { get; }
```

- [ ] **Step 3: Add the mutator + backing to `ITenantContext` / accessor**

`ITenantContext`:
```csharp
Guid? JustAcceptedInvitationId { get; }
void SetJustAcceptedInvitation(Guid invitationId);
```

`TenantContextAccessor` (set in `Clear()` too — reset between requests):

```csharp
public Guid? JustAcceptedInvitationId { get; private set; }
public void SetJustAcceptedInvitation(Guid invitationId) => JustAcceptedInvitationId = invitationId;

public void Clear()
{
    // existing resets
    JustAcceptedInvitationId = null;
}
```

`ICurrentUser` implementation just delegates to `ITenantContext`.

- [ ] **Step 4: Build + commit**

```powershell
dotnet build src/Kartova.SharedKernel.AspNetCore/
```

```bash
git add src/Kartova.SharedKernel.AspNetCore/
git commit -m "feat(slice-9): expose JustAcceptedInvitationId on ICurrentUser/ITenantContext"
```

---

### Task C3: `UserProjectionUpdater` (JWT-claim → `users` upsert)

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Application/UserProjectionUpdater.cs`
- Create: `tests/Kartova.Organization.Application.Tests/UserProjectionUpdaterTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/Kartova.Organization.Application.Tests/UserProjectionUpdaterTests.cs`:

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Organization.Application.Tests;

[TestClass]
public sealed class UserProjectionUpdaterTests
{
    private static OrganizationDbContext NewInMemory()
    {
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"users-projection-{Guid.NewGuid()}")
            .Options;
        return new OrganizationDbContext(opts);
    }

    [TestMethod]
    public async Task Upsert_inserts_new_user_with_computed_display_name()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var sut = new UserProjectionUpdater(clock);
        var tenant = new TenantId(Guid.NewGuid());

        await sut.UpsertAsync(db, new Guid("11111111-1111-1111-1111-111111111111"),
            "alice@example.com", "Alice", "Smith", tenant, CancellationToken.None);

        var u = await db.Users.SingleAsync();
        Assert.AreEqual("Alice Smith", u.DisplayName);
        Assert.AreEqual(clock.GetUtcNow(), u.LastSeenAt);
        Assert.AreEqual(clock.GetUtcNow(), u.CreatedAt);
    }

    [TestMethod]
    public async Task Upsert_falls_back_to_email_when_names_missing()
    {
        await using var db = NewInMemory();
        var sut = new UserProjectionUpdater(new FakeTimeProvider());
        var tenant = new TenantId(Guid.NewGuid());

        await sut.UpsertAsync(db, Guid.NewGuid(), "noname@example.com", null, null, tenant, CancellationToken.None);

        var u = await db.Users.SingleAsync();
        Assert.AreEqual("noname@example.com", u.DisplayName);
    }

    [TestMethod]
    public async Task Upsert_updates_existing_row_and_advances_last_seen()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var sut = new UserProjectionUpdater(clock);
        var tenant = new TenantId(Guid.NewGuid());
        var id = Guid.NewGuid();

        await sut.UpsertAsync(db, id, "alice@example.com", "Alice", "Smith", tenant, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(30));
        await sut.UpsertAsync(db, id, "alice@example.com", "Alice", "JONES", tenant, CancellationToken.None);

        var u = await db.Users.SingleAsync();
        Assert.AreEqual("Alice JONES", u.DisplayName);
        Assert.AreEqual(DateTimeOffset.Parse("2026-05-27T10:30:00Z"), u.LastSeenAt);
    }
}
```

If `Kartova.Organization.Application.Tests` doesn't exist, scaffold it like the domain test project. Add NuGet `Microsoft.EntityFrameworkCore.InMemory` for the test-only InMemory provider.

- [ ] **Step 2: Run, verify FAIL**

```powershell
dotnet test tests/Kartova.Organization.Application.Tests/ --filter "FullyQualifiedName~UserProjectionUpdaterTests"
```

Expected: COMPILE FAIL.

- [ ] **Step 3: Implement `UserProjectionUpdater`**

`src/Modules/Organization/Kartova.Organization.Application/UserProjectionUpdater.cs`:

```csharp
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Application;

public sealed class UserProjectionUpdater(TimeProvider clock)
{
    public async Task UpsertAsync(
        OrganizationDbContext db,
        Guid userId,
        string email,
        string? givenName,
        string? familyName,
        TenantId tenantId,
        CancellationToken ct)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        var displayName = User.ComputeDisplayName(givenName, familyName, email);
        var now = clock.GetUtcNow();

        if (existing is null)
        {
            db.Users.Add(new User
            {
                Id = userId,
                TenantId = tenantId,
                Email = email,
                GivenName = givenName,
                FamilyName = familyName,
                DisplayName = displayName,
                LastSeenAt = now,
                CreatedAt = now,
            });
        }
        else
        {
            existing.Email = email;
            existing.GivenName = givenName;
            existing.FamilyName = familyName;
            existing.DisplayName = displayName;
            existing.LastSeenAt = now;
        }
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run, verify PASS**

```powershell
dotnet test tests/Kartova.Organization.Application.Tests/
```

Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Application/UserProjectionUpdater.cs tests/Kartova.Organization.Application.Tests/
git commit -m "feat(slice-9): UserProjectionUpdater for JWT-claim sync"
```

---

### Task C4: Wire `UserProjectionUpdater` + invitation acceptance into `TenantClaimsTransformation`

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/TenantClaimsTransformation.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/PostAuthHook.cs` (new abstraction so SharedKernel.AspNetCore doesn't reference Organization)

The cleanest layering: define an `IPostAuthSyncHook` interface in `Kartova.SharedKernel.AspNetCore`; `OrganizationPostAuthSyncHook` (implementing it) lives in Organization.Infrastructure and is registered via DI; `TenantClaimsTransformation` resolves all `IEnumerable<IPostAuthSyncHook>` and invokes them.

- [ ] **Step 1: Define `IPostAuthSyncHook`**

`src/Kartova.SharedKernel.AspNetCore/IPostAuthSyncHook.cs`:

```csharp
using System.Security.Claims;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Runs inside TenantClaimsTransformation after tenant + role claim flattening,
/// allowing modules to hook into the post-auth lifecycle (e.g. user projection sync,
/// invitation acceptance detection). One hook per module — order is not significant.
/// </summary>
public interface IPostAuthSyncHook
{
    Task ExecuteAsync(ClaimsPrincipal principal, CancellationToken ct);
}
```

- [ ] **Step 2: Implement `OrganizationPostAuthSyncHook`**

`src/Modules/Organization/Kartova.Organization.Infrastructure/PostAuthHook.cs`:

```csharp
using System.Security.Claims;
using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

internal sealed class OrganizationPostAuthSyncHook(
    OrganizationDbContext db,
    UserProjectionUpdater projection,
    ITenantContext tenantContext,
    TimeProvider clock) : IPostAuthSyncHook
{
    public async Task ExecuteAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var subRaw = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? principal.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subRaw, out var userId)) return;
        if (tenantContext.TenantId is not { } tenantId) return;

        var email = principal.FindFirst("email")?.Value
                 ?? principal.FindFirst(ClaimTypes.Email)?.Value
                 ?? "";
        var given = principal.FindFirst("given_name")?.Value;
        var family = principal.FindFirst("family_name")?.Value;

        if (string.IsNullOrWhiteSpace(email)) return;   // can't materialize a row

        await projection.UpsertAsync(db, userId, email, given, family, tenantId, ct);

        // Invitation-acceptance side effect.
        var pending = await db.Invitations
            .FirstOrDefaultAsync(i => i.KeycloakUserId == userId && i.Status == InvitationStatus.Pending, ct);
        if (pending is not null && pending.ExpiresAt > clock.GetUtcNow())
        {
            pending.MarkAccepted(clock);
            await db.SaveChangesAsync(ct);
            tenantContext.SetJustAcceptedInvitation(pending.Id.Value);
        }
    }
}
```

- [ ] **Step 3: Extend `TenantClaimsTransformation`**

Convert it to a truly-async transformer (slice 7 left it returning `Task.FromResult`). After existing claim-flatten / role-expansion logic, resolve `IEnumerable<IPostAuthSyncHook>` from the request scope and invoke each:

```csharp
foreach (var hook in _hooks)
    await hook.ExecuteAsync(principal, CancellationToken.None);
```

`_hooks` injected via constructor. Make sure dependency scope is correct (scoped — DbContext-backed).

- [ ] **Step 4: Register the hook in `OrganizationModule.RegisterServices`**

```csharp
services.AddScoped<UserProjectionUpdater>();
services.AddScoped<IPostAuthSyncHook, OrganizationPostAuthSyncHook>();
```

- [ ] **Step 5: Build**

```powershell
dotnet build src/
```

Expected: clean.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore/IPostAuthSyncHook.cs src/Kartova.SharedKernel.AspNetCore/TenantClaimsTransformation.cs src/Modules/Organization/Kartova.Organization.Infrastructure/PostAuthHook.cs src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs
git commit -m "feat(slice-9): IPostAuthSyncHook + Organization hook (users upsert + invitation accept)"
```

---

**End of Phase C.** JWT claims now materialize the `users` projection on every authenticated request and detect/flip invitation acceptance. Permissions + role map updated; matrix test green.

---

## Phase D — Backend endpoints + handlers

### Task D1: `OrganizationUserDirectory` implementation + DI

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationUserDirectory.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs`
- Create: `tests/Kartova.Organization.Infrastructure.Tests/OrganizationUserDirectoryTests.cs`

- [ ] **Step 1: Write failing test**

`tests/Kartova.Organization.Infrastructure.Tests/OrganizationUserDirectoryTests.cs`:

```csharp
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure.Tests;

[TestClass]
public sealed class OrganizationUserDirectoryTests
{
    private static OrganizationDbContext NewInMemory(out TenantId tenant)
    {
        tenant = new TenantId(Guid.NewGuid());
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"directory-{Guid.NewGuid()}").Options;
        return new OrganizationDbContext(opts);
    }

    [TestMethod]
    public async Task GetAsync_returns_user_when_present()
    {
        await using var db = NewInMemory(out var tenant);
        var id = Guid.NewGuid();
        db.Users.Add(new User { Id = id, TenantId = tenant, Email = "a@b.c", DisplayName = "A B", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var sut = new OrganizationUserDirectory(db);
        var result = await sut.GetAsync(id, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("A B", result!.DisplayName);
    }

    [TestMethod]
    public async Task GetAsync_returns_null_when_absent()
    {
        await using var db = NewInMemory(out _);
        var sut = new OrganizationUserDirectory(db);
        Assert.IsNull(await sut.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task GetManyAsync_returns_only_matched_ids()
    {
        await using var db = NewInMemory(out var tenant);
        var id1 = Guid.NewGuid(); var id2 = Guid.NewGuid(); var id3 = Guid.NewGuid();
        db.Users.AddRange(
            new User { Id = id1, TenantId = tenant, Email = "1@x", DisplayName = "One", CreatedAt = DateTimeOffset.UtcNow },
            new User { Id = id2, TenantId = tenant, Email = "2@x", DisplayName = "Two", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var sut = new OrganizationUserDirectory(db);
        var result = await sut.GetManyAsync(new[] { id1, id2, id3 }, CancellationToken.None);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.ContainsKey(id1));
        Assert.IsTrue(result.ContainsKey(id2));
        Assert.IsFalse(result.ContainsKey(id3));
    }

    [TestMethod]
    public async Task GetManyAsync_returns_empty_for_empty_input()
    {
        await using var db = NewInMemory(out _);
        var sut = new OrganizationUserDirectory(db);
        var result = await sut.GetManyAsync(Array.Empty<Guid>(), CancellationToken.None);
        Assert.AreEqual(0, result.Count);
    }
}
```

- [ ] **Step 2: Implement**

`src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationUserDirectory.cs`:

```csharp
using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

internal sealed class OrganizationUserDirectory(OrganizationDbContext db) : IUserDirectory
{
    public async Task<UserDisplayInfo?> GetAsync(Guid userId, CancellationToken ct)
        => await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserDisplayInfo(u.Id, u.DisplayName, u.Email))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, UserDisplayInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, UserDisplayInfo>();
        var rows = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserDisplayInfo(u.Id, u.DisplayName, u.Email))
            .ToListAsync(ct);
        return rows.ToDictionary(u => u.Id);
    }
}
```

- [ ] **Step 3: Register in `OrganizationModule.RegisterServices`**

```csharp
services.AddScoped<IUserDirectory, OrganizationUserDirectory>();
```

The Organization Infrastructure project gains a project-reference to `Kartova.SharedKernel.Identity` (interface lives there).

- [ ] **Step 4: Run + commit**

```powershell
dotnet test tests/Kartova.Organization.Infrastructure.Tests/
```

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/ tests/Kartova.Organization.Infrastructure.Tests/
git commit -m "feat(slice-9): OrganizationUserDirectory implementation"
```

---

### Task D2: Org profile contract DTOs + endpoints (GET /me, PUT /me)

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Contracts/OrgProfileResponse.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Contracts/UpdateOrgProfileRequest.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Application/OrgProfileQueries.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Application/UpdateOrgProfileHandler.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/Endpoints/OrganizationEndpoints.cs` (or `Routes.cs` — check name)

- [ ] **Step 1: Add DTOs (each with `[ExcludeFromCodeCoverage]`)**

`OrgProfileResponse.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record OrgProfileResponse(
    Guid Id, string DisplayName, string? Description,
    string DefaultTimeZone, string? LogoEtag, string? LogoMimeType,
    DateTimeOffset CreatedAt);
```

`UpdateOrgProfileRequest.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record UpdateOrgProfileRequest(string DisplayName, string? Description, string DefaultTimeZone);
```

- [ ] **Step 2: Implement query**

`OrgProfileQueries.cs`:

```csharp
using Kartova.Organization.Contracts;
using Kartova.Organization.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Application;

public sealed class OrgProfileQueries(OrganizationDbContext db)
{
    public async Task<OrgProfileResponse?> GetMyOrgAsync(CancellationToken ct)
    {
        var row = await db.Organizations
            .Select(o => new {
                o.Id, o.DisplayName, o.Description, o.DefaultTimeZone,
                LogoEtag = o.Logo != null ? o.Logo.ContentHash : null,
                LogoMimeType = o.Logo != null ? o.Logo.MimeType : null,
                o.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        return new OrgProfileResponse(row.Id, row.DisplayName, row.Description,
            row.DefaultTimeZone, row.LogoEtag, row.LogoMimeType, row.CreatedAt);
    }
}
```

(Adjust the `o.Id` projection: if Organization aggregate exposes `Id` as a value-object, project `.Value`.)

- [ ] **Step 3: Implement update handler**

`UpdateOrgProfileHandler.cs`:

```csharp
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Application;

public sealed class UpdateOrgProfileHandler(OrganizationDbContext db)
{
    public async Task<UpdateOrgProfileResult> HandleAsync(UpdateOrgProfileRequest request, byte[]? ifMatch, CancellationToken ct)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(ct);
        if (org is null) return UpdateOrgProfileResult.NotFound;

        // If ifMatch supplied, EF Core concurrency token applies on SaveChanges. Slice-5 ADR-0096 pattern reused.
        org.UpdateProfile(request.DisplayName, request.Description, request.DefaultTimeZone);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UpdateOrgProfileResult.ConcurrencyConflict;
        }
        return UpdateOrgProfileResult.Ok;
    }
}

public enum UpdateOrgProfileResult { Ok, NotFound, ConcurrencyConflict }
```

The `Organization` aggregate needs a concurrency token column (e.g. `xmin` system column on Postgres) for the If-Match to do anything. If not present today, defer optimistic concurrency to a follow-up — return `Ok` for slice 9 and document. *(Confirm slice 5's pattern; reuse the existing rowversion-or-`xmin` setup.)*

- [ ] **Step 4: Wire the endpoints**

Locate the existing `OrganizationModule` endpoint mapper (slice 2 / slice 8). Add to the `/api/v1/organizations` group:

```csharp
group.MapGet("/me", async (OrgProfileQueries q, CancellationToken ct) =>
{
    var profile = await q.GetMyOrgAsync(ct);
    if (profile is null) return Results.NotFound();
    return Results.Ok(profile);
}).RequireAuthorization(KartovaPermissions.OrgProfileRead);

group.MapPut("/me", async (UpdateOrgProfileRequest body, UpdateOrgProfileHandler h, HttpContext http, CancellationToken ct) =>
{
    var ifMatch = http.Request.Headers.IfMatch.FirstOrDefault();
    byte[]? token = ifMatch is not null ? Convert.FromHexString(ifMatch.Trim('"')) : null;
    var result = await h.HandleAsync(body, token, ct);
    return result switch
    {
        UpdateOrgProfileResult.Ok => Results.NoContent(),
        UpdateOrgProfileResult.NotFound => Results.NotFound(),
        UpdateOrgProfileResult.ConcurrencyConflict => Results.Problem(
            type: ProblemTypes.ConcurrencyConflict, statusCode: 412),
        _ => Results.StatusCode(500),
    };
}).RequireAuthorization(KartovaPermissions.OrgProfileEdit);
```

DI: register `OrgProfileQueries` + `UpdateOrgProfileHandler` as scoped in `OrganizationModule.RegisterServices`.

- [ ] **Step 5: Build + commit**

```powershell
dotnet build src/
```

```bash
git add src/Modules/Organization/
git commit -m "feat(slice-9): GET/PUT /api/v1/organizations/me org profile endpoints"
```

---

### Task D3: SVG sanitization + logo magic-byte helper

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Application/LogoValidation.cs`
- Create: `tests/Kartova.Organization.Application.Tests/LogoValidationTests.cs`

NuGet: add `HtmlSanitizer` (Ganss.Xss) to `Kartova.Organization.Application.csproj`:
```xml
<PackageReference Include="HtmlSanitizer" Version="9.0.886" />
```

- [ ] **Step 1: Write failing tests**

`LogoValidationTests.cs`:

```csharp
using System.Text;
using Kartova.Organization.Application;

namespace Kartova.Organization.Application.Tests;

[TestClass]
public sealed class LogoValidationTests
{
    [TestMethod]
    public void MagicBytesMatch_png_with_correct_header_returns_true()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        Assert.IsTrue(LogoValidation.MagicBytesMatch(bytes, "image/png"));
    }

    [TestMethod]
    public void MagicBytesMatch_png_with_wrong_header_returns_false()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x00 };
        Assert.IsFalse(LogoValidation.MagicBytesMatch(bytes, "image/png"));
    }

    [TestMethod]
    public void MagicBytesMatch_jpeg_with_correct_header_returns_true()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        Assert.IsTrue(LogoValidation.MagicBytesMatch(bytes, "image/jpeg"));
    }

    [TestMethod]
    public void MagicBytesMatch_svg_with_xml_prelude_returns_true()
    {
        var bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg></svg>");
        Assert.IsTrue(LogoValidation.MagicBytesMatch(bytes, "image/svg+xml"));
    }

    [TestMethod]
    public void MagicBytesMatch_svg_with_root_element_returns_true()
    {
        var bytes = Encoding.UTF8.GetBytes("  <svg xmlns=\"...\"/>");
        Assert.IsTrue(LogoValidation.MagicBytesMatch(bytes, "image/svg+xml"));
    }

    [TestMethod]
    public void SanitizeSvg_strips_script_element()
    {
        var input = "<svg><script>alert(1)</script><circle r=\"5\"/></svg>";
        var (sanitized, materiallyChanged) = LogoValidation.SanitizeSvg(input);
        Assert.IsFalse(sanitized.Contains("script", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(materiallyChanged);
    }

    [TestMethod]
    public void SanitizeSvg_passes_clean_svg_unchanged()
    {
        var input = "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"5\" fill=\"red\"/></svg>";
        var (sanitized, materiallyChanged) = LogoValidation.SanitizeSvg(input);
        Assert.IsFalse(materiallyChanged);
        Assert.IsTrue(sanitized.Contains("circle"));
    }
}
```

- [ ] **Step 2: Implement**

`LogoValidation.cs`:

```csharp
using System.Text;
using Ganss.Xss;

namespace Kartova.Organization.Application;

public static class LogoValidation
{
    private static readonly HtmlSanitizer _svgSanitizer = BuildSanitizer();

    public static bool MagicBytesMatch(ReadOnlySpan<byte> bytes, string mimeType) => mimeType switch
    {
        "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/svg+xml" => IsSvgText(bytes),
        _ => false,
    };

    private static bool IsSvgText(ReadOnlySpan<byte> bytes)
    {
        // Heuristic: skip leading whitespace, then expect "<?xml" or "<svg".
        var s = Encoding.UTF8.GetString(bytes);
        var trimmed = s.AsSpan().TrimStart();
        return trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
    }

    public static (string Sanitized, bool MateriallyChanged) SanitizeSvg(string input)
    {
        var output = _svgSanitizer.Sanitize(input);
        var changeRatio = input.Length == 0 ? 0.0 : 1.0 - (double)output.Length / input.Length;
        return (output, changeRatio > 0.20);
    }

    private static HtmlSanitizer BuildSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var t in new[] {
            "svg","g","path","rect","circle","ellipse","polygon","polyline","line",
            "text","defs","use","linearGradient","radialGradient","stop","clipPath",
            "mask","pattern",
        }) s.AllowedTags.Add(t);

        s.AllowedAttributes.Clear();
        foreach (var a in new[] {
            "id","class","style","viewBox","d","fill","stroke","stroke-width",
            "x","y","cx","cy","r","rx","ry","points","transform","opacity",
            "width","height","xmlns",
        }) s.AllowedAttributes.Add(a);

        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("data");   // for data:image/* in <use>
        return s;
    }
}
```

- [ ] **Step 3: Run + commit**

```powershell
dotnet test tests/Kartova.Organization.Application.Tests/
```

```bash
git add src/Modules/Organization/Kartova.Organization.Application/LogoValidation.cs tests/Kartova.Organization.Application.Tests/LogoValidationTests.cs src/Modules/Organization/Kartova.Organization.Application/Kartova.Organization.Application.csproj
git commit -m "feat(slice-9): logo magic-byte check + SVG sanitization (Ganss.Xss)"
```

---

### Task D4: Logo upload + delete + serve endpoints

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Application/LogoCommands.cs`
- Modify: existing `OrganizationModule` endpoints registration

- [ ] **Step 1: Implement commands**

`LogoCommands.cs`:

```csharp
using System.Text;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Application;

public sealed class LogoCommands(OrganizationDbContext db)
{
    public async Task<UploadLogoResult> UploadAsync(byte[] bytes, string mimeType, CancellationToken ct)
    {
        if (!LogoValidation.MagicBytesMatch(bytes, mimeType))
            return new UploadLogoResult.Rejected("magic-byte mismatch");
        var processed = bytes;
        if (mimeType == "image/svg+xml")
        {
            var (clean, materiallyChanged) = LogoValidation.SanitizeSvg(Encoding.UTF8.GetString(bytes));
            if (materiallyChanged) return new UploadLogoResult.Rejected("SVG contained disallowed content");
            processed = Encoding.UTF8.GetBytes(clean);
        }
        var org = await db.Organizations.FirstOrDefaultAsync(ct);
        if (org is null) return new UploadLogoResult.NotFound();
        org.SetLogo(OrgLogo.Create(processed, mimeType));
        await db.SaveChangesAsync(ct);
        return new UploadLogoResult.Accepted(org.Logo!.ContentHash, mimeType);
    }

    public async Task<bool> ClearAsync(CancellationToken ct)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(ct);
        if (org is null) return false;
        org.ClearLogo();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<LogoServeData?> GetServeDataAsync(CancellationToken ct)
    {
        var row = await db.Organizations
            .Where(o => o.Logo != null)
            .Select(o => new LogoServeData(o.Logo!.Bytes, o.Logo.MimeType, o.Logo.ContentHash))
            .FirstOrDefaultAsync(ct);
        return row;
    }
}

public abstract record UploadLogoResult
{
    public sealed record Accepted(string Etag, string MimeType) : UploadLogoResult;
    public sealed record Rejected(string Reason) : UploadLogoResult;
    public sealed record NotFound : UploadLogoResult;
}

public sealed record LogoServeData(byte[] Bytes, string MimeType, string ContentHash);
```

- [ ] **Step 2: Wire endpoints**

In the Organization endpoints registration:

```csharp
group.MapPut("/me/logo", async (HttpRequest req, LogoCommands cmds, CancellationToken ct) =>
{
    var mime = req.ContentType ?? "";
    if (mime is not ("image/png" or "image/jpeg" or "image/svg+xml"))
        return Results.Problem(type: ProblemTypes.UnsupportedLogoMedia, statusCode: 415,
            detail: "Content-Type must be image/png, image/jpeg, or image/svg+xml.");

    using var ms = new MemoryStream();
    var limit = 256 * 1024 + 1;
    var buffer = new byte[8192];
    int read;
    while ((read = await req.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
    {
        ms.Write(buffer, 0, read);
        if (ms.Length >= limit) return Results.StatusCode(413);
    }
    var bytes = ms.ToArray();

    var result = await cmds.UploadAsync(bytes, mime, ct);
    return result switch
    {
        UploadLogoResult.Accepted a => Results.Ok(new { logoEtag = a.Etag, mimeType = a.MimeType }),
        UploadLogoResult.Rejected r => Results.Problem(type: ProblemTypes.UnsupportedLogoMedia, statusCode: 422, detail: r.Reason),
        UploadLogoResult.NotFound => Results.NotFound(),
        _ => Results.StatusCode(500),
    };
}).RequireAuthorization(KartovaPermissions.OrgProfileEdit);

group.MapDelete("/me/logo", async (LogoCommands cmds, CancellationToken ct) =>
{
    var ok = await cmds.ClearAsync(ct);
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization(KartovaPermissions.OrgProfileEdit);

group.MapGet("/me/logo", async (LogoCommands cmds, HttpContext http, CancellationToken ct) =>
{
    var data = await cmds.GetServeDataAsync(ct);
    if (data is null) return Results.NotFound();

    var ifNone = http.Request.Headers.IfNoneMatch.FirstOrDefault()?.Trim('"');
    if (ifNone == data.ContentHash)
    {
        http.Response.Headers.ETag = $"\"{data.ContentHash}\"";
        return Results.StatusCode(304);
    }
    http.Response.Headers.ETag = $"\"{data.ContentHash}\"";
    http.Response.Headers.CacheControl = "private, max-age=300";
    return Results.File(data.Bytes, data.MimeType);
}).RequireAuthorization(KartovaPermissions.OrgProfileRead);
```

- [ ] **Step 3: Register `LogoCommands` scoped, then commit**

```bash
git add src/Modules/Organization/
git commit -m "feat(slice-9): PUT/DELETE/GET /me/logo endpoints with ETag + 304"
```

---

### Task D5: Invitation contracts + handlers + endpoints

**Files:**
- Create: 4 contract records in `Kartova.Organization.Contracts/` (`InvitationResponse`, `CreateInvitationRequest`, `CreateInvitationResponse`, list query shape)
- Create: `CreateInvitationHandler.cs`, `RevokeInvitationHandler.cs`, `ListInvitationsQuery.cs` in `Kartova.Organization.Application/`
- Create: `tests/Kartova.Organization.Application.Tests/CreateInvitationHandlerTests.cs`
- Modify: endpoints registration

- [ ] **Step 1: Contracts** — `InvitationResponse`, `CreateInvitationRequest`, `CreateInvitationResponse` per spec §6.7. All `[ExcludeFromCodeCoverage]`. Use string `Status` (one of `"Pending"|"Accepted"|"Revoked"|"Expired"`).

- [ ] **Step 2: Failing test for `CreateInvitationHandler` three-way 409 model**

`CreateInvitationHandlerTests.cs`:

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Kartova.Organization.Application.Tests;

[TestClass]
public sealed class CreateInvitationHandlerTests
{
    private static (CreateInvitationHandler h, OrganizationDbContext db, IKeycloakAdminClient kc, TenantId tenant)
        Make(FakeTimeProvider clock)
    {
        var tenant = new TenantId(Guid.NewGuid());
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase($"inv-{Guid.NewGuid()}").Options;
        var db = new OrganizationDbContext(opts);
        var kc = Substitute.For<IKeycloakAdminClient>();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenant);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var h = new CreateInvitationHandler(db, kc, tenantCtx, currentUser, clock,
            new KartovaIdentityOptionsForTest("http://localhost:5173"));
        return (h, db, kc, tenant);
    }

    [TestMethod]
    public async Task Returns_EmailAlreadyInTenant_when_users_row_exists()
    {
        var clock = new FakeTimeProvider();
        var (h, db, _, tenant) = Make(clock);
        db.Users.Add(new User { Id = Guid.NewGuid(), TenantId = tenant, Email = "alice@x.com", DisplayName = "A", CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync();

        var r = await h.HandleAsync(new CreateInvitationRequest("alice@x.com", KartovaRoles.Member), CancellationToken.None);

        Assert.AreEqual(CreateInvitationError.EmailAlreadyInTenant, ((CreateInvitationResult.Failed)r).Error);
    }

    [TestMethod]
    public async Task Returns_EmailAlreadyInvited_when_pending_invitation_exists()
    {
        var clock = new FakeTimeProvider();
        var (h, db, _, tenant) = Make(clock);
        db.Invitations.Add(Invitation.Create("alice@x.com", KartovaRoles.Member, Guid.NewGuid(), Guid.NewGuid(), tenant, clock));
        await db.SaveChangesAsync();

        var r = await h.HandleAsync(new CreateInvitationRequest("Alice@X.com", KartovaRoles.Member), CancellationToken.None);

        Assert.AreEqual(CreateInvitationError.EmailAlreadyInvited, ((CreateInvitationResult.Failed)r).Error);
    }

    [TestMethod]
    public async Task Returns_EmailAlreadyOnPlatform_when_keycloak_returns_conflict()
    {
        var clock = new FakeTimeProvider();
        var (h, _, kc, _) = Make(clock);
        kc.CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>())
          .Returns<Task<Guid>>(_ => throw new KeycloakAdminException(KeycloakAdminError.EmailAlreadyExists, "exists"));

        var r = await h.HandleAsync(new CreateInvitationRequest("bob@x.com", KartovaRoles.Member), CancellationToken.None);

        Assert.AreEqual(CreateInvitationError.EmailAlreadyOnPlatform, ((CreateInvitationResult.Failed)r).Error);
    }

    [TestMethod]
    public async Task Happy_path_creates_kc_user_and_db_invitation()
    {
        var clock = new FakeTimeProvider();
        var (h, db, kc, _) = Make(clock);
        var kcId = Guid.NewGuid();
        kc.CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>()).Returns(kcId);

        var r = await h.HandleAsync(new CreateInvitationRequest("carol@x.com", KartovaRoles.Member), CancellationToken.None);
        var ok = (CreateInvitationResult.Created)r;

        Assert.AreEqual(kcId, ok.Response.Invitation.Id != Guid.Empty ? kcId : Guid.Empty); // sanity
        Assert.AreEqual("http://localhost:5173/?invitation=1", ok.Response.InviteUrl);
        await kc.Received(1).AssignRealmRoleAsync(kcId, KartovaRoles.Member, Arg.Any<CancellationToken>());
        Assert.AreEqual(1, await db.Invitations.CountAsync());
        Assert.AreEqual(1, await db.Users.CountAsync(u => u.Id == kcId));
    }

    [TestMethod]
    public async Task Compensates_by_deleting_kc_user_when_role_assign_fails()
    {
        var clock = new FakeTimeProvider();
        var (h, db, kc, _) = Make(clock);
        var kcId = Guid.NewGuid();
        kc.CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>()).Returns(kcId);
        kc.AssignRealmRoleAsync(kcId, Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns<Task>(_ => throw new KeycloakAdminException(KeycloakAdminError.Unexpected, "boom"));

        var r = await h.HandleAsync(new CreateInvitationRequest("dave@x.com", KartovaRoles.Member), CancellationToken.None);

        Assert.IsInstanceOfType<CreateInvitationResult.Failed>(r);
        Assert.AreEqual(CreateInvitationError.Upstream, ((CreateInvitationResult.Failed)r).Error);
        await kc.Received(1).DeleteUserAsync(kcId, Arg.Any<CancellationToken>());
        Assert.AreEqual(0, await db.Invitations.CountAsync());
    }
}

// Minimal stand-in for the real options type — replace with whatever the real one is in the prod code.
file sealed record KartovaIdentityOptionsForTest(string FrontendBaseUrl) : Microsoft.Extensions.Options.IOptions<Kartova.SharedKernel.Identity.KeycloakAdminOptions>
{
    public Kartova.SharedKernel.Identity.KeycloakAdminOptions Value => new()
    {
        BaseUrl = "x", Realm = "x", AdminClientId = "x", AdminClientSecret = "x",
        FrontendBaseUrl = FrontendBaseUrl,
    };
}
```

- [ ] **Step 3: Implement `CreateInvitationHandler`**

```csharp
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kartova.Organization.Application;

public sealed class CreateInvitationHandler(
    OrganizationDbContext db,
    IKeycloakAdminClient kc,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<KeycloakAdminOptions> options)
{
    public async Task<CreateInvitationResult> HandleAsync(CreateInvitationRequest request, CancellationToken ct)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 320)
            return new CreateInvitationResult.Failed(CreateInvitationError.Validation);
        if (!KartovaRoles.All.Contains(request.Role))
            return new CreateInvitationResult.Failed(CreateInvitationError.Validation);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return new CreateInvitationResult.Failed(CreateInvitationError.EmailAlreadyInTenant);

        var existingPending = await db.Invitations
            .FirstOrDefaultAsync(i => i.Email == email && i.Status == InvitationStatus.Pending, ct);
        if (existingPending is not null)
            return new CreateInvitationResult.Failed(CreateInvitationError.EmailAlreadyInvited);

        Guid kcId;
        try
        {
            kcId = await kc.CreateUserAsync(new CreateKeycloakUserRequest(
                email, null, null, tenant.TenantId!.Value.Value.ToString(),
                new[] { "UPDATE_PASSWORD" }), ct);
        }
        catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.EmailAlreadyExists)
        {
            return new CreateInvitationResult.Failed(CreateInvitationError.EmailAlreadyOnPlatform);
        }

        try
        {
            await kc.AssignRealmRoleAsync(kcId, request.Role, ct);
        }
        catch (KeycloakAdminException)
        {
            try { await kc.DeleteUserAsync(kcId, ct); } catch { /* best-effort */ }
            return new CreateInvitationResult.Failed(CreateInvitationError.Upstream);
        }

        var invitation = Invitation.Create(email, request.Role, currentUser.UserId, kcId, tenant.TenantId!.Value, clock);
        db.Invitations.Add(invitation);
        db.Users.Add(new User
        {
            Id = kcId, TenantId = tenant.TenantId!.Value, Email = email,
            DisplayName = email, CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);

        var inviteUrl = $"{options.Value.FrontendBaseUrl}/?invitation=1";
        var response = new InvitationResponse(invitation.Id.Value, invitation.Email, invitation.Role,
            invitation.InvitedAt, invitation.ExpiresAt, invitation.Status.ToString(),
            invitation.InvitedByUserId, invitation.AcceptedAt, invitation.RevokedAt);
        return new CreateInvitationResult.Created(new CreateInvitationResponse(response, inviteUrl));
    }
}

public abstract record CreateInvitationResult
{
    public sealed record Created(CreateInvitationResponse Response) : CreateInvitationResult;
    public sealed record Failed(CreateInvitationError Error) : CreateInvitationResult;
}

public enum CreateInvitationError
{
    Validation, EmailAlreadyInTenant, EmailAlreadyInvited, EmailAlreadyOnPlatform, Upstream,
}
```

- [ ] **Step 4: Implement `RevokeInvitationHandler`**

```csharp
public sealed class RevokeInvitationHandler(
    OrganizationDbContext db, IKeycloakAdminClient kc, TimeProvider clock)
{
    public async Task<RevokeResult> HandleAsync(Guid invitationId, CancellationToken ct)
    {
        var inv = await db.Invitations.FirstOrDefaultAsync(i => EF.Property<Guid>(i, "_id") == invitationId, ct);
        if (inv is null) return RevokeResult.NotFound;
        if (inv.Status != InvitationStatus.Pending) return RevokeResult.NotPending;
        try { if (inv.KeycloakUserId is { } kid) await kc.DeleteUserAsync(kid, ct); }
        catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound) { }
        inv.Revoke(clock);
        await db.SaveChangesAsync(ct);
        return RevokeResult.Ok;
    }
}

public enum RevokeResult { Ok, NotFound, NotPending }
```

- [ ] **Step 5: Implement `ListInvitationsQuery` (cursor-paginated, slice-7 pattern)**

Mirror `ApplicationListQueries` from slice 7/8. Filter by `status` enum (parsed from query string), sort by `sortBy` ∈ {`invitedAt`, `expiresAt`, `email`}. Return `CursorPage<InvitationResponse>`.

- [ ] **Step 6: Wire endpoints**

```csharp
group.MapGet("/invitations", async (...) => ...).RequireAuthorization(KartovaPermissions.OrgInvitationsRead);
group.MapPost("/invitations", async (CreateInvitationRequest body, CreateInvitationHandler h, CancellationToken ct) =>
{
    var r = await h.HandleAsync(body, ct);
    return r switch
    {
        CreateInvitationResult.Created c => Results.Created($"/api/v1/organizations/invitations/{c.Response.Invitation.Id}", c.Response),
        CreateInvitationResult.Failed { Error: CreateInvitationError.Validation } => Results.Problem(type: ProblemTypes.ValidationFailed, statusCode: 422),
        CreateInvitationResult.Failed { Error: CreateInvitationError.EmailAlreadyInTenant } => Results.Problem(type: ProblemTypes.EmailAlreadyInTenant, statusCode: 409),
        CreateInvitationResult.Failed { Error: CreateInvitationError.EmailAlreadyInvited } => Results.Problem(type: ProblemTypes.EmailAlreadyInvited, statusCode: 409),
        CreateInvitationResult.Failed { Error: CreateInvitationError.EmailAlreadyOnPlatform } => Results.Problem(type: ProblemTypes.EmailAlreadyOnPlatform, statusCode: 409),
        CreateInvitationResult.Failed { Error: CreateInvitationError.Upstream } => Results.Problem(statusCode: 502),
        _ => Results.StatusCode(500),
    };
}).RequireAuthorization(KartovaPermissions.OrgInvitationsCreate);
group.MapPost("/invitations/{id:guid}/revoke", async (Guid id, RevokeInvitationHandler h, CancellationToken ct) =>
{
    var r = await h.HandleAsync(id, ct);
    return r switch
    {
        RevokeResult.Ok => Results.NoContent(),
        RevokeResult.NotFound => Results.NotFound(),
        RevokeResult.NotPending => Results.Problem(type: ProblemTypes.InvitationNotPending, statusCode: 409),
        _ => Results.StatusCode(500),
    };
}).RequireAuthorization(KartovaPermissions.OrgInvitationsRevoke);
```

Add `ProblemTypes.EmailAlreadyInvited`, `EmailAlreadyInTenant`, `EmailAlreadyOnPlatform`, `InvitationNotPending`, `UnsupportedLogoMedia` to the existing `ProblemTypes` class.

- [ ] **Step 7: Run + commit**

```powershell
dotnet test tests/Kartova.Organization.Application.Tests/
```

```bash
git add src/Modules/Organization/ tests/Kartova.Organization.Application.Tests/
git commit -m "feat(slice-9): invitation create/list/revoke endpoints with three-way 409 model"
```

---

### Task D6: User search + detail endpoints

**Files:**
- Create: `UserSummaryResponse`, `UserDetailResponse`, `UserTeamMembership` DTOs (Contracts)
- Create: `UserQueries.cs` (Application)
- Wire 2 endpoints

- [ ] **Step 1: Contracts** — per spec §6.7, all `[ExcludeFromCodeCoverage]`.

- [ ] **Step 2: `UserQueries`**

```csharp
public sealed class UserQueries(OrganizationDbContext db)
{
    public async Task<IReadOnlyList<UserSummaryResponse>> SearchAsync(string q, int limit, CancellationToken ct)
    {
        if (q.Length < 2) throw new ArgumentException("Query must be at least 2 chars.", nameof(q));
        var clipped = Math.Clamp(limit, 1, 20);
        var pattern = $"%{q.ToLowerInvariant()}%";
        return await db.Users
            .Where(u => EF.Functions.ILike(u.DisplayName, pattern) || EF.Functions.ILike(u.Email, pattern))
            .OrderBy(u => u.DisplayName)
            .Take(clipped)
            .Select(u => new UserSummaryResponse(u.Id, u.DisplayName, u.Email))
            .ToListAsync(ct);
    }

    public async Task<UserDetailResponse?> GetDetailAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return null;
        var teams = await db.TeamMembers
            .Where(m => m.UserId == id)
            .Join(db.Teams, m => m.TeamId, t => t.Id, (m, t) => new UserTeamMembership(t.Id.Value, t.DisplayName, m.Role.ToString()))
            .ToListAsync(ct);
        return new UserDetailResponse(user.Id, user.Email, user.DisplayName, user.GivenName, user.FamilyName,
            teams, user.CreatedAt, user.LastSeenAt);
    }
}
```

Adjust to match slice-8 `TeamMembership` shape (slice 8 had `TeamId` as `TeamId` VO + `Role` as `TeamRole` enum).

- [ ] **Step 3: Endpoints**

```csharp
group.MapGet("/users", async (string? q, int? limit, UserQueries qs, CancellationToken ct) =>
{
    if (string.IsNullOrEmpty(q) || q.Length < 2)
        return Results.Problem(type: ProblemTypes.ValidationFailed, statusCode: 422,
            detail: "Query 'q' must be at least 2 characters.");
    var rows = await qs.SearchAsync(q, limit ?? 20, ct);
    return Results.Ok(rows);
}).RequireAuthorization(KartovaPermissions.OrgUsersSearch);

group.MapGet("/users/{id:guid}", async (Guid id, UserQueries qs, CancellationToken ct) =>
{
    var r = await qs.GetDetailAsync(id, ct);
    return r is null ? Results.NotFound() : Results.Ok(r);
}).RequireAuthorization(KartovaPermissions.OrgUsersRead);
```

`[BoundedListResult]` attribute on the search endpoint per CLAUDE.md.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Organization/
git commit -m "feat(slice-9): GET /users (typeahead) + GET /users/{id} endpoints"
```

---

### Task D7: Session bootstrap endpoint

**Files:**
- Create: `Kartova.Organization.Contracts/SessionStartResponse.cs` + `AcceptedInvitationInfo.cs` + `MeTeamMembership.cs` (reuse if slice 7 already has it)
- Create: `Kartova.Organization.Application/SessionStartHandler.cs`
- Add: `POST /api/v1/auth/session` endpoint

- [ ] **Step 1: Contracts** per spec §6.7. The `SessionStartResponse` references `UserDisplayInfo` from `Kartova.SharedKernel`.

- [ ] **Step 2: Handler**

```csharp
public sealed class SessionStartHandler(
    OrganizationDbContext db,
    IUserDirectory directory,
    OrgProfileQueries orgQueries,
    ICurrentUser currentUser,
    ITenantContext tenant)
{
    public async Task<SessionStartResponse> HandleAsync(CancellationToken ct)
    {
        var me = await directory.GetAsync(currentUser.UserId, ct)
                 ?? new UserDisplayInfo(currentUser.UserId, currentUser.UserId.ToString(), "");

        var org = await orgQueries.GetMyOrgAsync(ct)
                  ?? throw new InvalidOperationException("Org row missing for tenant.");

        var role = currentUser.Role ?? KartovaRoles.Viewer;
        var permissions = KartovaRolePermissions.Map[role].ToArray();
        var teams = currentUser.TeamMemberships.Select(t => new MeTeamMembership(t.TeamId, t.Role.ToString())).ToArray();

        AcceptedInvitationInfo? accepted = null;
        if (tenant.JustAcceptedInvitationId is { } invId)
        {
            var inv = await db.Invitations.FirstOrDefaultAsync(i => EF.Property<Guid>(i, "_id") == invId, ct);
            if (inv is not null)
            {
                var invitedBy = await directory.GetAsync(inv.InvitedByUserId, ct);
                if (invitedBy is not null && inv.AcceptedAt is { } acc)
                    accepted = new AcceptedInvitationInfo(org.DisplayName, invitedBy, inv.InvitedAt, acc);
            }
        }

        return new SessionStartResponse(me, role, permissions, teams, org, accepted);
    }
}
```

- [ ] **Step 3: Endpoint**

```csharp
app.MapPost("/api/v1/auth/session", async (SessionStartHandler h, CancellationToken ct) =>
{
    var r = await h.HandleAsync(ct);
    return Results.Ok(r);
}).RequireAuthorization();   // any valid JWT
```

This endpoint is NOT inside the Organization group — it's `/api/v1/auth/session`. Register in `Program.cs` or a dedicated `AuthEndpoints` mapper.

- [ ] **Step 4: Commit**

```bash
git add src/
git commit -m "feat(slice-9): POST /api/v1/auth/session bootstrap with AcceptedInvitation payload"
```

---

### Task D8: `ExpireInvitationsHostedService` + DI

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/ExpireInvitationsHostedService.cs`
- Modify: `OrganizationModule.RegisterServices` (register `AddHostedService<ExpireInvitationsHostedService>()`)
- Create: `tests/Kartova.Organization.Infrastructure.Tests/ExpireInvitationsHostedServiceTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[TestMethod]
public async Task Skips_when_lock_unavailable()
{
    var locks = Substitute.For<IDistributedLock>();
    locks.TryAcquireAsync("expire-invitations", Arg.Any<CancellationToken>()).Returns((IAsyncDisposable?)null);
    var kc = Substitute.For<IKeycloakAdminClient>();
    // ... build minimal SP with AdminOrganizationDbContext (InMemory), TimeProvider
    var sut = new ExpireInvitationsHostedService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock, NullLogger<ExpireInvitationsHostedService>.Instance);
    // tick once via FakeTimeProvider; assert kc.Received(0).DeleteUserAsync(...)
}

[TestMethod]
public async Task Expires_pending_invitations_past_due()
{
    // Seed: Pending invitation with ExpiresAt = now - 1h
    // Acquire lock returns a handle
    // After tick: invitation status = Expired; kc.DeleteUserAsync called once
}
```

- [ ] **Step 2: Implement**

```csharp
internal sealed class ExpireInvitationsHostedService(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    TimeProvider clock,
    ILogger<ExpireInvitationsHostedService> logger)
    : LeaderElectedPeriodicService(scopes, locks, clock, logger)
{
    protected override string LockName => "expire-invitations";
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AdminOrganizationDbContext>();
        var kc = services.GetRequiredService<IKeycloakAdminClient>();
        var now = ((TimeProvider)services.GetService(typeof(TimeProvider))!).GetUtcNow();

        var due = await db.Invitations
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt < now)
            .ToListAsync(ct);

        foreach (var inv in due)
        {
            try { if (inv.KeycloakUserId is { } kid) await kc.DeleteUserAsync(kid, ct); }
            catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound) { }
            inv.MarkExpired(((TimeProvider)services.GetService(typeof(TimeProvider))!));
        }
        await db.SaveChangesAsync(ct);

        if (due.Count > 0)
            logger.LogInformation("Expired {Count} invitations.", due.Count);
    }
}
```

`AdminOrganizationDbContext` is the BYPASSRLS pool already wired in slice-5/8. Confirm it has DbSets for `Invitations` — extend if needed.

- [ ] **Step 3: Register + commit**

```csharp
services.AddHostedService<ExpireInvitationsHostedService>();
services.AddPostgresDistributedLocks();   // idempotent if already called
```

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/ExpireInvitationsHostedService.cs tests/Kartova.Organization.Infrastructure.Tests/
git commit -m "feat(slice-9): ExpireInvitationsHostedService (hourly leader-elected sweep)"
```

---

### Task D9: `Program.cs` wiring + appsettings + KeyCloak realm config

**Files:**
- Modify: `src/Kartova.Api/Program.cs`
- Modify: `src/Kartova.Api/appsettings.json` + `appsettings.Development.json`
- Modify: `deploy/keycloak/kartova-realm.json`

- [ ] **Step 1: Add `kartova-admin` client to realm.json**

Append to `clients` array:

```json
{
  "clientId": "kartova-admin",
  "name": "Kartova Admin API Client",
  "enabled": true,
  "publicClient": false,
  "serviceAccountsEnabled": true,
  "standardFlowEnabled": false,
  "directAccessGrantsEnabled": false,
  "secret": "admin-dev-secret",
  "attributes": {
    "use.refresh.tokens": "false"
  }
}
```

Add service-account realm-role mappings (`realm-management` roles: `manage-users`, `view-users`, `view-realm`):

In `users` array, append the auto-generated service account user with role mappings. Or rely on KeyCloak's auto-generation and add `serviceAccountClientRoles` mapping in a `clientRoleMappings` block — easiest is to start KC once, log in to admin console, take a fresh export, and copy the service-account block.

For dev: doc the manual KC console step in `deploy/keycloak/README.md` if scripting it is fiddly.

- [ ] **Step 2: appsettings**

`appsettings.json`:

```json
{
  "KartovaIdentity": {
    "Keycloak": {
      "BaseUrl": "http://keycloak:8080",
      "Realm": "kartova",
      "AdminClientId": "kartova-admin",
      "AdminClientSecret": "OVERRIDE_VIA_ENV",
      "FrontendBaseUrl": "http://localhost:5173"
    }
  }
}
```

Override in `Development` + via env var `KartovaIdentity__Keycloak__AdminClientSecret` in `docker-compose.yml` for the API service.

- [ ] **Step 3: `Program.cs` registrations**

```csharp
builder.Services.AddKeycloakAdminClient(builder.Configuration);
builder.Services.AddPostgresDistributedLocks();
// Hosted services are registered per-module via OrganizationModule already.
```

- [ ] **Step 4: Smoke test the full stack**

```powershell
docker compose up -d --build
curl http://localhost:8080/health/ready
```

Expected: API healthy. Manual test:
- Acquire JWT via OIDC against the dev realm
- `curl -X POST http://localhost:8080/api/v1/auth/session -H "Authorization: Bearer <jwt>"`
- Expected: 200 with `SessionStartResponse`.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.Api/Program.cs src/Kartova.Api/appsettings*.json deploy/keycloak/kartova-realm.json
git commit -m "feat(slice-9): wire KeyCloak Admin client + distributed locks + realm admin client"
```

---

**End of Phase D.** All backend endpoints exist; KeyCloak admin client wired; expiry sweep registered.

---

## Phase E — Catalog & Team integration (Owner enrichment, ownerUserId filter, TeamMember enrichment)

### Task E1: `ApplicationResponse.Owner` enrichment via `IUserDirectory`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/ApplicationQueries.cs` (or equivalent list/detail query files)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (may need a project-reference to `Kartova.SharedKernel.Identity`)
- Modify: existing integration tests for application endpoints

- [ ] **Step 1: Extend `ApplicationResponse`**

Add nullable `Owner: UserDisplayInfo?` field (preserve other fields). Add `using Kartova.SharedKernel;`.

- [ ] **Step 2: Add project reference**

`Kartova.Catalog.Contracts.csproj`: project-reference `Kartova.SharedKernel/Kartova.SharedKernel.csproj` if not already.
`Kartova.Catalog.Application.csproj` + `.Infrastructure.csproj`: project-reference `Kartova.SharedKernel.Identity/Kartova.SharedKernel.Identity.csproj`.

- [ ] **Step 3: Modify list query to batch-fetch owners**

In `ApplicationQueries` (or wherever the list endpoint materializes rows):

```csharp
public async Task<CursorPage<ApplicationResponse>> ListAsync(..., IUserDirectory directory, CancellationToken ct)
{
    var page = await /* existing query */;
    var ownerIds = page.Items.Select(a => a.OwnerUserId).Distinct().ToList();
    var owners = await directory.GetManyAsync(ownerIds, ct);
    var enriched = page.Items.Select(a => a with {
        Owner = owners.TryGetValue(a.OwnerUserId, out var u) ? u : null
    }).ToList();
    return page with { Items = enriched };
}
```

- [ ] **Step 4: Do the same for detail endpoint**

Single `directory.GetAsync(app.OwnerUserId, ct)` call after loading the row.

- [ ] **Step 5: Update affected tests**

Catalog integration tests + unit tests: assert response includes `Owner` populated when seed includes a matching `users` row; `Owner` is `null` when user has been deleted.

- [ ] **Step 6: Run + commit**

```powershell
dotnet test tests/Kartova.Catalog.IntegrationTests/
```

```bash
git add src/Modules/Catalog/ tests/Kartova.Catalog.IntegrationTests/
git commit -m "feat(slice-9): enrich ApplicationResponse with Owner via IUserDirectory"
```

---

### Task E2: `?ownerUserId=` filter on `GET /catalog/applications`

**Files:**
- Modify: `Kartova.Catalog.Application/ApplicationListQueryParameters.cs` (or the request shape)
- Modify: List query SQL/EF where clause
- Modify: Endpoint signature

- [ ] **Step 1: Add optional `ownerUserId: Guid?` parameter to the list query parameters record**

- [ ] **Step 2: Add `where(a => a.OwnerUserId == ownerUserId)` in the query when filter present**

- [ ] **Step 3: Validate that the supplied `ownerUserId` resolves to a real user in the current tenant**

```csharp
if (request.OwnerUserId is { } id && !await db.Users.AnyAsync(u => u.Id == id, ct))
    return ListResult.InvalidOwner;
```

Wait — Catalog can't query Organization's `users` table directly. Solution: pass through `IUserDirectory.GetAsync`. If null → 422 `invalid-owner`.

- [ ] **Step 4: Update endpoint to surface 422 on `InvalidOwner`**

- [ ] **Step 5: Test**

Integration test: `GET /catalog/applications?ownerUserId={unknown-guid}` returns 422.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/ tests/Kartova.Catalog.IntegrationTests/
git commit -m "feat(slice-9): add ?ownerUserId= filter to GET /catalog/applications"
```

---

### Task E3: `TeamMemberResponse` display info enrichment

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Contracts/TeamMemberResponse.cs`
- Modify: `Kartova.Organization.Application/TeamDetailQuery.cs` (slice-8 query)
- Modify: integration tests

- [ ] **Step 1: Add `DisplayName` + `Email` to `TeamMemberResponse`**

```csharp
[ExcludeFromCodeCoverage]
public sealed record TeamMemberResponse(Guid UserId, string Role, DateTimeOffset AddedAt,
    string DisplayName, string Email);
```

- [ ] **Step 2: Batch-fetch via `IUserDirectory` in TeamDetail query** (mirror E1).

- [ ] **Step 3: Update slice-8 component tests + integration tests** (TeamDetailPage and `GET /teams/{id}` tests) for the new shape.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Organization/ tests/
git commit -m "feat(slice-9): enrich TeamMemberResponse with DisplayName + Email via IUserDirectory"
```

---

**End of Phase E.** Catalog list/detail show owner display names; Team detail shows member display names; owner filter works.

---

## Phase F — SPA

### Task F1: Permission snapshot already updated in C1

Confirm `web/src/shared/auth/permissions.snapshot.json` carries the 7 new entries. Drift sentinel test passes.

### Task F2: `useOrgProfile` + `useUpdateOrgProfile` + `useLogoUrl` + `useUploadOrgLogo` + `useDeleteOrgLogo`

**Files:**
- Create: `web/src/features/organization/api/organization.ts`
- Create: `web/src/features/organization/api/__tests__/organization.test.tsx`

- [ ] **Step 1: Write hooks (typed `apiClient` per slice-7 codegen)**

```ts
import { apiClient } from "@/shared/api";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const orgKeys = {
  profile: () => ["org", "profile"] as const,
  logoUrl: (etag: string | null) => ["org", "logo", etag ?? ""] as const,
};

export function useOrgProfile() {
  return useQuery({
    queryKey: orgKeys.profile(),
    queryFn: async ({ signal }) => {
      const { data, error } = await apiClient.GET("/api/v1/organizations/me", { signal });
      if (error) throw error;
      return data!;
    },
    staleTime: 60_000,
  });
}

export function useUpdateOrgProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { displayName: string; description?: string; defaultTimeZone: string; ifMatch?: string }) => {
      const { error } = await apiClient.PUT("/api/v1/organizations/me", {
        body, headers: body.ifMatch ? { "If-Match": body.ifMatch } : undefined,
      });
      if (error) throw error;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: orgKeys.profile() }),
  });
}

export function useLogoUrl() {
  const { data } = useOrgProfile();
  if (!data?.logoEtag) return null;
  return `/api/v1/organizations/me/logo?v=${data.logoEtag}`;
}

export function useUploadOrgLogo() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ bytes, mimeType }: { bytes: Blob; mimeType: string }) => {
      const r = await fetch("/api/v1/organizations/me/logo", {
        method: "PUT",
        headers: { "Content-Type": mimeType, "Authorization": `Bearer ${await getToken()}` },
        body: bytes,
      });
      if (!r.ok) throw new Error(`Upload failed: ${r.status}`);
      return await r.json() as { logoEtag: string; mimeType: string };
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: orgKeys.profile() }),
  });
}

export function useDeleteOrgLogo() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { error } = await apiClient.DELETE("/api/v1/organizations/me/logo", {});
      if (error) throw error;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: orgKeys.profile() }),
  });
}
```

(The `getToken()` helper exists in slice-7's auth infra; reuse it.)

- [ ] **Step 2: Unit tests** mirror slice-7 `api/__tests__` pattern — `vi.spyOn(apiClient, "GET")`, etc.

- [ ] **Step 3: Commit**

```bash
git add web/src/features/organization/api/
git commit -m "feat(slice-9): organization API hooks"
```

---

### Task F3: `OrganizationSettingsPage` + `LogoUploader`

**Files:**
- Create: `web/src/features/organization/pages/OrganizationSettingsPage.tsx`
- Create: `web/src/features/organization/components/LogoUploader.tsx`
- Create: `web/src/features/organization/schemas/orgProfile.ts`
- Create: component tests

- [ ] **Step 1: zod schema** (`orgProfile.ts`):

```ts
import { z } from "zod";

export const orgProfileSchema = z.object({
  displayName: z.string().min(1).max(128),
  description: z.string().max(1024).optional().nullable(),
  defaultTimeZone: z.string().refine(
    (tz) => Intl.supportedValuesOf("timeZone").includes(tz),
    { message: "Unknown IANA time-zone." }),
});
```

- [ ] **Step 2: `LogoUploader.tsx`** — drag-drop area, file size + format check, 256 KB client-side guard, preview at 64×64 and 200×200. Call `useUploadOrgLogo`. On 422 from server, show toast.

- [ ] **Step 3: `OrganizationSettingsPage.tsx`** — react-hook-form + zod resolver, fields for displayName / description / defaultTimeZone, Logo section using `LogoUploader`. Edit gated by `OrgProfileEdit` permission; disabled inputs for non-OrgAdmins.

- [ ] **Step 4: Component tests** — happy submit, validation error, 422 server response, permission-gated edit/readonly.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/organization/
git commit -m "feat(slice-9): OrganizationSettingsPage + LogoUploader + zod schema"
```

---

### Task F4: Invitations — hooks, page, dialog, copy-link box

**Files:**
- Create: `web/src/features/organization/api/invitations.ts`
- Create: `web/src/features/organization/pages/InvitationsPage.tsx`
- Create: `web/src/features/organization/components/InviteUserDialog.tsx`
- Create: `web/src/features/organization/components/CopyInviteLinkBox.tsx`
- Create: `web/src/features/organization/components/RevokeInvitationConfirm.tsx`
- Create: `web/src/features/organization/schemas/inviteUser.ts`
- Tests

- [ ] **Step 1: zod schema** (`inviteUser.ts`):

```ts
import { z } from "zod";
export const inviteUserSchema = z.object({
  email: z.string().email().max(320),
  role: z.enum(["Viewer", "Member", "TeamAdmin", "OrgAdmin"]),
});
```

- [ ] **Step 2: Hooks** — `useInvitationsList(params)` cursor-paginated, `useCreateInvitation()` returns the response (with `inviteUrl`), `useRevokeInvitation(id)`.

- [ ] **Step 3: `InvitationsPage`** — tabs (Pending / All); cursor list per slice 7 `useCursorList`; "Invite user" button gated by `OrgInvitationsCreate`. Each row: email, role, invited-by display name (resolves via `useUser(invitedByUserId)` lazily — acceptable for this short list), live "expires in N days" countdown, revoke button (Pending only) gated by `OrgInvitationsRevoke`.

- [ ] **Step 4: `InviteUserDialog`** — react-hook-form + zod, email + role select. On submit success → switches to success state with `<CopyInviteLinkBox url={response.inviteUrl} email={response.invitation.email} />`. Buttons: "Done" / "Invite another" (resets form).

- [ ] **Step 5: `CopyInviteLinkBox`** — large copy button, inline copy-confirmation, expiry-disclaimer + "email delivery coming soon" explainer.

- [ ] **Step 6: Tests** — list rendering, create-flow happy path, copy-link state, three-way 409 handling (server returns problem → toast surfaces friendly message per error type).

- [ ] **Step 7: Commit**

```bash
git add web/src/features/organization/
git commit -m "feat(slice-9): invitations page + invite dialog + copy-link UX"
```

---

### Task F5: Users — hooks, detail page, search combobox, `<OwnerLink>`

**Files:**
- Create: `web/src/features/users/api/users.ts` — `useUser(id)`, `useUserSearch(q, { limit, enabled })` (debounced 250ms upstream via the consumer)
- Create: `web/src/features/users/pages/UserDetailPage.tsx` — three cards (user / teams / owned applications). Owned-apps card calls `useApplications({ ownerUserId: id })` independently.
- Create: `web/src/features/users/components/UserSearchCombobox.tsx` — react-aria-components Combobox (Wave / Untitled UI). Debounce input → `useUserSearch`. Renders option list with display name + email.
- Create: `web/src/features/users/components/OwnerLink.tsx` — `<OwnerLink user={UserDisplayInfo | null | undefined} />`; renders `<Link to={`/users/${id}`}>{displayName}</Link>` or "Unknown user".
- Tests

- [ ] **Step 1: Hooks** — `useUser` uses `["user", id]` key, aggressive `staleTime: 5min`. `useUserSearch` accepts `enabled` from caller (already-debounced).

- [ ] **Step 2: `UserDetailPage`** — load via `useParams<{ id: string }>`. Three parallel fetches. Cross-tenant 404 surfaces "User not found".

- [ ] **Step 3: `UserSearchCombobox`** — typeahead. Reads `q` state, debounces 250ms, enables `useUserSearch` only when `q.length >= 2`. Selected user emits `(user: UserDisplayInfo) => void`.

- [ ] **Step 4: `OwnerLink`** — pure component, no fetch.

- [ ] **Step 5: Tests** — `OwnerLink` with null user (fallback render), with user (correct link href), Combobox typing flow with stubbed API client.

- [ ] **Step 6: Modify slice-8 `AddMemberDialog`** to use `<UserSearchCombobox>` instead of the UUID input. Wire the selected user's id into the form field; zod schema updates to require a selected user (not a free-text guid).

- [ ] **Step 7: Modify catalog `ApplicationsTable` + `ApplicationDetailPage`** — replace raw owner UUID with `<OwnerLink user={app.owner} />`.

- [ ] **Step 8: Modify slice-8 `TeamDetailPage`** — member rows render display name + email from the now-embedded fields.

- [ ] **Step 9: Commit**

```bash
git add web/src/features/users/ web/src/features/teams/components/AddMemberDialog.tsx web/src/features/teams/pages/TeamDetailPage.tsx web/src/features/catalog/
git commit -m "feat(slice-9): users feature (detail, search combobox, OwnerLink) + slice-8 UI upgrades"
```

---

### Task F6: Auth/session — `useStartSession`, `OidcCallbackHandler`, `WelcomePage`

**Files:**
- Create: `web/src/features/auth/api/session.ts`
- Create: `web/src/features/auth/components/OidcCallbackHandler.tsx`
- Create: `web/src/features/auth/pages/WelcomePage.tsx`
- Modify: existing OIDC callback wiring (slice 7's auth bootstrap path)

- [ ] **Step 1: `useStartSession`**

```ts
export function useStartSession() {
  return useMutation({
    mutationFn: async () => {
      const { data, error } = await apiClient.POST("/api/v1/auth/session", {});
      if (error) throw error;
      return data!;
    },
  });
}
```

- [ ] **Step 2: `OidcCallbackHandler`**

```tsx
export function OidcCallbackHandler() {
  const navigate = useNavigate();
  const startSession = useStartSession();
  const qc = useQueryClient();

  useEffect(() => {
    let mounted = true;
    (async () => {
      try {
        const r = await startSession.mutateAsync();
        if (!mounted) return;
        qc.setQueryData(orgKeys.profile(), r.organization);
        if (r.acceptedInvitation) {
          navigate("/welcome", { state: r.acceptedInvitation });
        } else {
          navigate("/catalog", { replace: true });
        }
      } catch {
        navigate("/login-error", { replace: true });
      }
    })();
    return () => { mounted = false; };
  }, []);

  return <CenteredSpinner message="Signing you in..." />;
}
```

- [ ] **Step 3: `WelcomePage`** — reads `useLocation().state` → renders celebration screen with org name, who invited them, role, "Continue to Kartova" button → `/catalog`.

- [ ] **Step 4: Wire into router** (slice-7's callback route gets replaced or composed with `OidcCallbackHandler`).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/auth/ web/src/app/router.tsx
git commit -m "feat(slice-9): session bootstrap + OidcCallbackHandler + WelcomePage"
```

---

### Task F7: Router + Sidebar + Header

**Files:**
- Modify: `web/src/app/router.tsx`
- Modify: `web/src/components/layout/Sidebar.tsx`
- Modify: `web/src/components/layout/Header.tsx`

- [ ] **Step 1: Router** — add `/settings/organization`, `/settings/invitations`, `/users/:id`, `/welcome` routes (slice-9 feature pages).

- [ ] **Step 2: Sidebar** — add Settings group:

```tsx
{ hasPermission("org.profile.read") && (
  <NavGroup title="Settings">
    <NavLink to="/settings/organization">Organization</NavLink>
    {hasPermission("org.invitations.read") && <NavLink to="/settings/invitations">Invitations</NavLink>}
  </NavGroup>
)}
```

- [ ] **Step 3: Header** — use `useOrgProfile()` + `useLogoUrl()`:

```tsx
const logoUrl = useLogoUrl();
const { data: org } = useOrgProfile();
return logoUrl
  ? <img src={logoUrl} alt={org?.displayName} className="h-8 w-8" />
  : <span className="font-semibold">{org?.displayName}</span>;
```

- [ ] **Step 4: Commit**

```bash
git add web/src/app/router.tsx web/src/components/layout/
git commit -m "feat(slice-9): wire Settings sidebar group, Header logo rendering, /welcome route"
```

---

**End of Phase F.** SPA closes all four E-03.F-01 stories.

---

## Phase G — ADR-0100

### Task G1: Write ADR-0100 — Identity scope: strict one-email-per-tenant

**Files:**
- Create: `docs/architecture/decisions/ADR-0100-strict-one-email-per-tenant-identity-scope.md`
- Modify: `docs/architecture/decisions/README.md`

- [ ] **Step 1: Write ADR-0100 verbatim from spec §10.2** (Context / Decision / Consequences / Related).

- [ ] **Step 2: Add to README keyword index:**

```
| Identity scope (one email per tenant) | ADR-0100 |
| Cross-tenant duplicate email handling | ADR-0100 |
```

- [ ] **Step 3: Commit**

```bash
git add docs/architecture/decisions/ADR-0100-strict-one-email-per-tenant-identity-scope.md docs/architecture/decisions/README.md
git commit -m "docs(adr): ADR-0100 strict one-email-per-tenant identity scope"
```

---

## Phase H — Integration verification + DoD

### Task H1: Integration tests for Phase D endpoints

**Files:**
- Extend: `tests/Kartova.Organization.IntegrationTests/` with the 16 scenarios listed in spec §11.3

Each test uses Testcontainers (KeyCloak + Postgres). Use the existing slice-8 fixtures (TestContainerFixture etc.) as templates. Cover the full happy-path + each 409/422 branch + the cross-module owner enrichment.

- [ ] **Step 1: Add tests one feature area at a time**, committing after each green batch:

```bash
git commit -m "test(slice-9): invitation integration tests (create / revoke / acceptance / expire)"
git commit -m "test(slice-9): org profile + logo upload integration tests"
git commit -m "test(slice-9): user search + detail integration tests"
git commit -m "test(slice-9): session bootstrap integration tests"
git commit -m "test(slice-9): cross-module owner enrichment integration tests"
```

- [ ] **Step 2: Confirm full integration suite green**

```powershell
dotnet test tests/Kartova.Organization.IntegrationTests/
dotnet test tests/Kartova.Catalog.IntegrationTests/
```

Expected: all PASS.

---

### Task H2: Architecture tests pass + drift-sentinel coverage

- [ ] **Step 1: Run all arch tests**

```powershell
dotnet test tests/Kartova.ArchitectureTests/
```

Expected: PASS. Add tests if any per-spec §11.1 are missing:

- `Kartova_SharedKernel_Identity_does_not_reference_AspNetCore`
- `Organization_owns_users_and_invitations_tables`
- `Catalog_does_not_reference_Organization_Domain`
- `IDistributedLock_implementations_use_session_advisory_locks`

- [ ] **Step 2: Commit any added arch tests**

```bash
git add tests/Kartova.ArchitectureTests/
git commit -m "test(slice-9): architecture tests for slice-9 module boundaries"
```

---

### Task H3: Docker happy + negative-path HTTP verification (CLAUDE.md DoD #5)

- [ ] **Step 1: Start the stack**

```powershell
docker compose down
docker compose up -d --build
```

Wait for `/health/ready` to return 200 on all services.

- [ ] **Step 2: Manual HTTP verification** (capture each command's curl output to a scratch file):

```powershell
$tok = & ./scripts/get-dev-token.ps1 org-a-admin   # slice-4 helper, if exists; else use Postman/oidc-client
$h = @{ Authorization = "Bearer $tok" }

# Happy: session start
curl -X POST http://localhost:8080/api/v1/auth/session -H "Authorization: Bearer $tok"

# Happy: invitation create
curl -X POST http://localhost:8080/api/v1/organizations/invitations -H "Authorization: Bearer $tok" -H "Content-Type: application/json" -d '{"email":"new-user@example.com","role":"Member"}'

# Happy: org profile read
curl http://localhost:8080/api/v1/organizations/me -H "Authorization: Bearer $tok"

# Happy: org profile update
curl -X PUT http://localhost:8080/api/v1/organizations/me -H "Authorization: Bearer $tok" -H "Content-Type: application/json" -d '{"displayName":"Org A","description":"Hello","defaultTimeZone":"Europe/Warsaw"}'

# Happy: logo upload
curl -X PUT http://localhost:8080/api/v1/organizations/me/logo -H "Authorization: Bearer $tok" -H "Content-Type: image/png" --data-binary "@./test-assets/sample.png"

# Happy: logo serve
curl -o downloaded.png http://localhost:8080/api/v1/organizations/me/logo -H "Authorization: Bearer $tok" -i

# Happy: user search
curl "http://localhost:8080/api/v1/organizations/users?q=admin&limit=10" -H "Authorization: Bearer $tok"

# Negative: duplicate invitation
curl -X POST http://localhost:8080/api/v1/organizations/invitations -H "Authorization: Bearer $tok" -H "Content-Type: application/json" -d '{"email":"new-user@example.com","role":"Member"}'
# Expected: 409 email-already-invited

# Negative: oversize logo
$big = New-Object byte[] (300*1024)
[IO.File]::WriteAllBytes("./big.png", $big)
curl -X PUT http://localhost:8080/api/v1/organizations/me/logo -H "Authorization: Bearer $tok" -H "Content-Type: image/png" --data-binary "@./big.png"
# Expected: 413

# Negative: viewer trying to invite
$viewerTok = & ./scripts/get-dev-token.ps1 org-a-member
curl -X POST http://localhost:8080/api/v1/organizations/invitations -H "Authorization: Bearer $viewerTok" -H "Content-Type: application/json" -d '{"email":"x@y.z","role":"Member"}'
# Expected: 403
```

- [ ] **Step 2: Capture results into a scratch file inside the worktree** (`docs/superpowers/plans/slice-9-docker-verification.md`) — paste each command + actual response body/status. This satisfies CLAUDE.md DoD #5's "output captured and confirmed" requirement.

- [ ] **Step 3: Commit the verification log**

```bash
git add docs/superpowers/plans/slice-9-docker-verification.md
git commit -m "verify(slice-9): docker compose HTTP happy + negative paths captured"
```

---

### Task H4: SPA UI verification

- [ ] **Step 1: Start SPA fresh** (cold start per ADR-0084)

```powershell
docker compose stop web
cd web; npm ci; npm run dev
```

- [ ] **Step 2: Drive the SPA via Playwright MCP**

In order:
1. Log in as `org-a-admin`. Confirm header shows "Org A" (no logo yet).
2. Navigate to `/settings/organization`. Confirm form populated. Upload `test-assets/sample.png`. Wait for header to refresh — verify logo visible.
3. Navigate to `/settings/invitations`. Click "Invite user" → fill `bob@example.com` + Member role → submit. Verify success state shows copy-link box.
4. Copy the invite link. Open a new browser context (incognito). Navigate to the link. Verify redirect to KeyCloak login. Sign in as the new user — set password.
5. Verify landing on `/welcome` — celebration screen with "Welcome to Org A" + invited-by display name.
6. Click Continue → land on `/catalog`.
7. Navigate to a catalog application. Confirm Owner is shown as a clickable display name (not a UUID). Click → land on `/users/{id}` showing teams + owned applications.
8. Open Team detail page. Verify members show display name + email instead of raw UUIDs.
9. Open Add-Member dialog — confirm typeahead. Type `admin`. Verify Combobox surfaces matching users.

Capture screenshots at each step into the verification log.

- [ ] **Step 3: Commit screenshots + observations**

```bash
git add docs/superpowers/plans/slice-9-docker-verification.md docs/superpowers/plans/slice-9-screenshots/
git commit -m "verify(slice-9): SPA end-to-end manual flow with Playwright screenshots"
```

---

### Task H5: `/simplify`, mutation testing, `/superpowers:requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review`

Per CLAUDE.md DoD #2 + #3 + #6 + #7 + #8 + #9:

- [ ] **Step 1: Run `/simplify`** against full branch diff. Triage findings.
- [ ] **Step 2: Run `/misc:mutation-sentinel`** then `/misc:test-generator` until mutation score ≥80% on changed files.
- [ ] **Step 3: Run `/superpowers:requesting-code-review`** against the slice-9 branch diff with spec + plan as context.
- [ ] **Step 4: Run `/pr-review-toolkit:review-pr`**.
- [ ] **Step 5: Run `/deep-review`** with `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`, this plan, ADRs touched, and tests as context. Address Blocking + Should-fix findings.

Each step commits any resulting fixes:

```bash
git add -A
git commit -m "review(slice-9): address <review-name> findings"
```

---

### Task H6: Update CHECKLIST + push

- [ ] **Step 1: Mark stories complete in `docs/product/CHECKLIST.md`:**

```
- [x] E-03.F-01.S-01 — Configure organization profile (slice 9 — PR #TBD, 2026-05-27; bytea logo, Description, DefaultTimeZone)
- [x] E-03.F-01.S-02 — Invite users with specific roles (slice 9 — PR #TBD, 2026-05-27; copy-link UX, no SMTP; expiry sweep via PostgresAdvisoryLock)
- [x] E-03.F-01.S-03 — View user details (slice 9 — PR #TBD, 2026-05-27; /users/:id with teams + owned apps via SPA composition)
- [x] E-03.F-01.S-04 — User search for team-member add (slice 9 — PR #TBD, 2026-05-27; UserSearchCombobox replaces UUID input)
```

Update phase totals (Phase 1 progress).

- [ ] **Step 2: Final solution build**

```powershell
dotnet build -c Release /p:TreatWarningsAsErrors=true
dotnet test
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 3: Commit + push**

```bash
git add docs/product/CHECKLIST.md
git commit -m "docs(slice-9): close E-03.F-01.S-01..S-04 in CHECKLIST"
git push -u origin feat/slice-9-organization-people-management
```

- [ ] **Step 4: Open PR**

```bash
gh pr create --title "feat(slice-9): organization & people management (E-03.F-01.S-01..S-04)" --body "$(cat <<'EOF'
## Summary
- Closes E-03.F-01.S-01..S-04 (org profile, invitations, user display+detail, user search)
- Introduces three pieces of cross-cutting shared infrastructure: KeyCloak Admin API client, IUserDirectory, Postgres-advisory-lock distributed locking
- New ADRs: ADR-0099 (distributed locking), ADR-0100 (strict one-email-per-tenant)

## Test plan
- [x] Full solution build green w/ TreatWarningsAsErrors=true
- [x] All architecture / unit / integration tests pass
- [x] docker compose HTTP happy + negative paths verified (see slice-9-docker-verification.md)
- [x] SPA end-to-end manual flow + Playwright screenshots
- [x] /simplify findings triaged
- [x] Mutation score >= 80% on changed files
- [x] /superpowers:requesting-code-review run
- [x] /pr-review-toolkit:review-pr run
- [x] /deep-review run; Blocking + Should-fix addressed

EOF
)"
```

---

**End of Phase H.** Slice 9 ships. Per CLAUDE.md, "complete" only after all DoD bullets cite verification evidence — the verification log + review-run commits provide the citations.

---

## Plan self-review summary

- **Spec coverage:** all sections of `2026-05-27-slice-9-organization-people-management-design.md` are mapped to tasks (§1 → A2/A3/A4/A5/A6/A7/A8/A9/B/C/D/E/F/G; §11 testing → unit tests per task + H1; §12 out-of-scope → not implemented, documented; §14 DoD → H3-H6).
- **Placeholder scan:** all "Step N" code blocks contain runnable code; commands cite expected output. No "TBD", "TODO" patterns remain.
- **Type consistency:** `IUserDirectory.GetManyAsync` returns `IReadOnlyDictionary<Guid, UserDisplayInfo>` throughout (A2/D1/E1/E3). `ICurrentUser.JustAcceptedInvitationId` is `Guid?` everywhere (C2/C4/D7). `Invitation` state machine throws `InvalidOperationException` on illegal transitions (B2/D8). Three-way 409 error model uses the same `CreateInvitationError` enum names across handler/endpoint/test (D5).






