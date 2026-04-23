# Slice 2 Implementation Plan — Auth + Multi-Tenancy

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire end-to-end authenticated + tenant-scoped requests on the Kartova backend. KeyCloak in docker-compose with a seeded realm, JWT validation middleware, a reusable `ITenantScope` abstraction (transaction-bound `SET LOCAL app.current_tenant_id`), a new `Organization` module with RLS-enforced reads, role-based authorization on one marker endpoint, an admin bypass path with a separate BYPASSRLS DbContext, and RFC 7807 problem-details for all errors. Testing via a local RSA test JWT signer plus one KeyCloak testcontainer auth smoke test.

**Architecture:** New sharedkernel projects split by tech concern — `Kartova.SharedKernel` stays tech-agnostic; `Kartova.SharedKernel.Postgres` holds the `TenantScope` implementation + EF interceptors + `AddModuleDbContext<T>` helper; `Kartova.SharedKernel.AspNetCore` holds JWT auth extensions + claims transformation + the endpoint filter that calls `Begin`/`CommitAsync` around tenant-scoped routes; `Kartova.SharedKernel.Wolverine` contains the parallel middleware skeleton for future Kafka/async work. The `Organization` module follows the same Domain/Application/Infrastructure/Contracts Clean-Architecture layout as `Catalog`, with a dedicated `Infrastructure.Admin` sub-assembly for the BYPASSRLS `POST /organizations` endpoint. Integration tests use a local RSA `TestJwtSigner` for speed; one separate `Kartova.Api.IntegrationTests` project runs a single auth-smoke test against a real KeyCloak testcontainer.

**Tech Stack:** .NET 10 LTS · ASP.NET Core · EF Core 10 · Npgsql · KeyCloak (Quay official image, pinned) · xUnit + FluentAssertions + Testcontainers.PostgreSql + Testcontainers.Keycloak · NetArchTest.Rules · WolverineFx 5.32 · Microsoft.AspNetCore.Authentication.JwtBearer 10 · System.IdentityModel.Tokens.Jwt / Microsoft.IdentityModel.Tokens 8

**Pre-execution flags (confirm before running plan):**

- [ ] **KeyCloak image pin:** plan pins `quay.io/keycloak/keycloak:26.1`. If a newer 26.x stable exists at implementation time, bump and retest.
- [ ] **Dev passwords:** realm JSON uses `dev_pass` for all seeded users. Local-dev-only; not a secret.
- [ ] **`kartova_bypass_rls` role password:** `dev_only` in `postgres/init.sql`. Local-only.
- [ ] **Test signer issuer:** `https://test-issuer.kartova.local` — static string; must match both signer config and JwtBearer validation parameters configured by `TestJwtAuthenticationDefaults`.
- [ ] **Realm client id:** `kartova-api` (public client, direct access grants enabled for dev ROPC).
- [ ] **`NpgsqlDataSource` availability:** plan assumes `Npgsql.DependencyInjection` registers it; confirmed in Slice 1.

---

## Task 1: Save ADR-0090 and update indexes

**Goal:** Capture the tenant-scope mechanism as an accepted ADR before any code is written. Update README index + change log + CLAUDE.md working agreements.

**Files:**
- Create: `docs/architecture/decisions/ADR-0090-tenant-scope-mechanism.md`
- Modify: `docs/architecture/decisions/README.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Create ADR-0090**

File: `docs/architecture/decisions/ADR-0090-tenant-scope-mechanism.md`

Contents: copy the ADR-0090 draft from `docs/superpowers/specs/2026-04-22-slice-2-auth-multitenancy-design.md` section 10 verbatim (header "ADR-0090: ..." onward). Status stays `Accepted`, Date `2026-04-22`.

- [ ] **Step 2: Insert row in README ADR table**

In `docs/architecture/decisions/README.md`, locate the row for ADR-0089 and insert a new row **before** ADR-0091:

```
| [0090](ADR-0090-tenant-scope-mechanism.md) | Tenant Scope Mechanism — Transaction-Bound `SET LOCAL` with Shared Connection per Request | Multi-Tenancy | Accepted | 0006, 0011, 0012, 0014, 0080, 0082 | `ITenantScope` owns one connection + tx per request; `SET LOCAL app.current_tenant_id` at Begin; commit via transport adapter before response/ack. All module DbContexts share the scope's connection + enlist in the tx. |
```

- [ ] **Step 3: Add ADR-0090 to "Multi-Tenancy" category**

Change:
```
- **Multi-Tenancy**: 0011, 0012, 0013, 0014
```
to:
```
- **Multi-Tenancy**: 0011, 0012, 0013, 0014, 0090
```

- [ ] **Step 4: Append change-log entry**

Append after the last history row:
```
| 2026-04-22 | ADR-0090 (Tenant scope mechanism) accepted — `ITenantScope` with transaction-bound `SET LOCAL`, shared connection per request, per-transport adapters; Slice 2 starts |
```

- [ ] **Step 5: Update CLAUDE.md working agreements**

In `CLAUDE.md`, in the "Working agreements" bullet list, insert a new bullet after the "Before adding features" bullet:

```
- **Tenant scope & DB access:** All tenant-scoped DB work runs inside `ITenantScope` (one open connection + transaction per request, `SET LOCAL app.current_tenant_id` on `Begin`). Register module DbContexts via `AddModuleDbContext<T>` — never raw `AddDbContext` for tenant-owned data. Transport adapters (ASP.NET endpoint filter, Wolverine/Kafka middleware) call `Begin`/`CommitAsync` — handlers never touch the scope. See ADR-0090.
```

In the "Key architectural decisions" table, add a row after the "Database" row:
```
| Tenant scope | One connection + tx per request, `SET LOCAL` on begin, commit before response | ADR-0090 |
```

- [ ] **Step 6: Commit**

```bash
git add docs/architecture/decisions/ADR-0090-tenant-scope-mechanism.md docs/architecture/decisions/README.md CLAUDE.md
git commit -m "docs(adr): ADR-0090 — tenant-scope mechanism + CLAUDE.md working agreement"
```

---

## Task 2: Add `kartova_bypass_rls` DB role

**Goal:** Add a BYPASSRLS PostgreSQL role to the local dev init script, used only by the admin bypass DbContext for `POST /api/v1/admin/organizations`.

**Files:**
- Modify: `docker/postgres/init.sql`

- [ ] **Step 1: Append role creation to init.sql**

Append to end of `docker/postgres/init.sql`:

```sql

-- ADR-0090 admin bypass path: BYPASSRLS role used exclusively by
-- AdminOrganizationDbContext for POST /api/v1/admin/organizations.
-- Enforced to that assembly by architecture tests.
CREATE ROLE kartova_bypass_rls WITH LOGIN PASSWORD 'dev_only' BYPASSRLS;
GRANT CONNECT ON DATABASE kartova TO kartova_bypass_rls;
GRANT USAGE, CREATE ON SCHEMA public TO kartova_bypass_rls;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO kartova_bypass_rls;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kartova_bypass_rls;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO kartova_bypass_rls;
```

- [ ] **Step 2: Commit**

```bash
git add docker/postgres/init.sql
git commit -m "feat(db): add kartova_bypass_rls role for admin bypass path"
```

Note: the role takes effect on a fresh volume (`docker compose down -v && docker compose up -d postgres`). Manual apply for existing volume will be documented in Task 26 before CI run.

---

## Task 3: Create KeyCloak realm JSON

**Goal:** Commit a realm config that KeyCloak imports on startup: one realm `kartova`, one public client `kartova-api` with direct access grants for ROPC dev flow, two orgs, three users with `tenant_id` user attribute, one platform-admin user without tenant, and claim mappers emitting `tenant_id` at the top level of the access token.

**Files:**
- Create: `deploy/keycloak/kartova-realm.json`

Stable seeded UUIDs (must match `Kartova.Testing.Auth.SeededOrgs` in Task 12):
- Org A: `11111111-1111-1111-1111-111111111111`
- Org B: `22222222-2222-2222-2222-222222222222`

- [x] **Step 1: Create realm JSON**

File: `deploy/keycloak/kartova-realm.json`

```json
{
  "realm": "kartova",
  "enabled": true,
  "sslRequired": "none",
  "registrationAllowed": false,
  "accessTokenLifespan": 900,
  "ssoSessionIdleTimeout": 1800,
  "ssoSessionMaxLifespan": 36000,
  "roles": {
    "realm": [
      { "name": "OrgAdmin" },
      { "name": "Member" },
      { "name": "platform-admin" }
    ]
  },
  "clients": [
    {
      "clientId": "kartova-api",
      "enabled": true,
      "publicClient": true,
      "directAccessGrantsEnabled": true,
      "standardFlowEnabled": true,
      "serviceAccountsEnabled": false,
      "redirectUris": ["http://localhost:5173/*", "http://localhost:8080/*"],
      "webOrigins": ["http://localhost:5173", "http://localhost:8080"],
      "attributes": {
        "access.token.lifespan": "900"
      },
      "protocolMappers": [
        {
          "name": "tenant_id",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "user.attribute": "tenant_id",
            "claim.name": "tenant_id",
            "jsonType.label": "String",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "userinfo.token.claim": "true"
          }
        }
      ]
    }
  ],
  "users": [
    {
      "username": "admin@orga.kartova.local",
      "enabled": true,
      "emailVerified": true,
      "email": "admin@orga.kartova.local",
      "firstName": "Alice",
      "lastName": "Admin",
      "attributes": {
        "tenant_id": ["11111111-1111-1111-1111-111111111111"]
      },
      "credentials": [
        { "type": "password", "value": "dev_pass", "temporary": false }
      ],
      "realmRoles": ["OrgAdmin"]
    },
    {
      "username": "member@orga.kartova.local",
      "enabled": true,
      "emailVerified": true,
      "email": "member@orga.kartova.local",
      "firstName": "Mike",
      "lastName": "Member",
      "attributes": {
        "tenant_id": ["11111111-1111-1111-1111-111111111111"]
      },
      "credentials": [
        { "type": "password", "value": "dev_pass", "temporary": false }
      ],
      "realmRoles": ["Member"]
    },
    {
      "username": "admin@orgb.kartova.local",
      "enabled": true,
      "emailVerified": true,
      "email": "admin@orgb.kartova.local",
      "firstName": "Bob",
      "lastName": "Admin",
      "attributes": {
        "tenant_id": ["22222222-2222-2222-2222-222222222222"]
      },
      "credentials": [
        { "type": "password", "value": "dev_pass", "temporary": false }
      ],
      "realmRoles": ["OrgAdmin"]
    },
    {
      "username": "platform-admin@kartova.local",
      "enabled": true,
      "emailVerified": true,
      "email": "platform-admin@kartova.local",
      "firstName": "Platform",
      "lastName": "Admin",
      "credentials": [
        { "type": "password", "value": "dev_pass", "temporary": false }
      ],
      "realmRoles": ["platform-admin"]
    }
  ]
}
```

- [x] **Step 2: Commit**

```bash
git add deploy/keycloak/kartova-realm.json
git commit -m "feat(keycloak): seed realm with 2 orgs + 3 users + platform-admin for Slice 2"
```

---

## Task 4: Add KeyCloak services to docker-compose

**Goal:** Extend `docker-compose.yml` with `keycloak-db` (Postgres for KeyCloak's own storage) + `keycloak` service with realm-import on start. Wire `api` to KeyCloak via OIDC env vars.

**Files:**
- Modify: `docker-compose.yml`

- [x] **Step 1: Replace `docker-compose.yml` entirely**

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

  keycloak-db:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_USER: keycloak
      POSTGRES_PASSWORD: keycloak_dev
      POSTGRES_DB: keycloak
    volumes:
      - keycloak-db-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U keycloak -d keycloak"]
      interval: 5s
      timeout: 3s
      retries: 20
      start_period: 5s

  keycloak:
    image: quay.io/keycloak/keycloak:26.1
    restart: unless-stopped
    command: ["start-dev", "--import-realm"]
    environment:
      KC_DB: postgres
      KC_DB_URL: jdbc:postgresql://keycloak-db:5432/keycloak
      KC_DB_USERNAME: keycloak
      KC_DB_PASSWORD: keycloak_dev
      KEYCLOAK_ADMIN: admin
      KEYCLOAK_ADMIN_PASSWORD: admin_dev
      KC_HOSTNAME_STRICT: "false"
      KC_HTTP_ENABLED: "true"
    ports:
      - "8180:8080"
    volumes:
      - ./deploy/keycloak/kartova-realm.json:/opt/keycloak/data/import/kartova-realm.json:ro
    depends_on:
      keycloak-db:
        condition: service_healthy
    healthcheck:
      test: ["CMD-SHELL", "exec 3<>/dev/tcp/localhost/8080 && echo -e 'GET /realms/kartova/.well-known/openid-configuration HTTP/1.1\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q 200"]
      interval: 10s
      timeout: 5s
      retries: 30
      start_period: 20s

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
      ConnectionStrings__KartovaBypass: "Host=postgres;Port=5432;Database=kartova;Username=kartova_bypass_rls;Password=dev_only"
      Authentication__Authority: "http://keycloak:8080/realms/kartova"
      Authentication__MetadataAddress: "http://keycloak:8080/realms/kartova/.well-known/openid-configuration"
      Authentication__Audience: "kartova-api"
      Authentication__RequireHttpsMetadata: "false"
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
      keycloak:
        condition: service_healthy
    restart: unless-stopped

volumes:
  postgres-data:
  keycloak-db-data:
```

- [x] **Step 2: Commit**

```bash
git add docker-compose.yml
git commit -m "feat(compose): add KeyCloak + keycloak-db services, wire API to OIDC"
```

---

## Task 5: Create new shared-kernel and test auth csproj projects; add to slnx

**Goal:** Scaffold the four new csprojs — `Kartova.SharedKernel.Postgres`, `Kartova.SharedKernel.AspNetCore`, `Kartova.SharedKernel.Wolverine`, `Kartova.Testing.Auth` — with empty `Class1`-style placeholders to verify builds, then add to `Kartova.slnx`.

**Files:**
- Create: `src/Kartova.SharedKernel.Postgres/Kartova.SharedKernel.Postgres.csproj`
- Create: `src/Kartova.SharedKernel.Postgres/Placeholder.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj`
- Create: `src/Kartova.SharedKernel.AspNetCore/Placeholder.cs`
- Create: `src/Kartova.SharedKernel.Wolverine/Kartova.SharedKernel.Wolverine.csproj`
- Create: `src/Kartova.SharedKernel.Wolverine/Placeholder.cs`
- Create: `tests/Kartova.Testing.Auth/Kartova.Testing.Auth.csproj`
- Create: `tests/Kartova.Testing.Auth/Placeholder.cs`
- Modify: `Kartova.slnx`

- [x] **Step 1: Create `Kartova.SharedKernel.Postgres.csproj`**

File: `src/Kartova.SharedKernel.Postgres/Kartova.SharedKernel.Postgres.csproj`

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
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.0" />
    <PackageReference Include="Npgsql" Version="10.0.0" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageReference Include="Npgsql.DependencyInjection" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

File: `src/Kartova.SharedKernel.Postgres/Placeholder.cs`

```csharp
namespace Kartova.SharedKernel.Postgres;

internal static class Placeholder { }
```

- [x] **Step 2: Create `Kartova.SharedKernel.AspNetCore.csproj`**

File: `src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj`

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
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
    <ProjectReference Include="..\Kartova.SharedKernel.Postgres\Kartova.SharedKernel.Postgres.csproj" />
  </ItemGroup>

</Project>
```

File: `src/Kartova.SharedKernel.AspNetCore/Placeholder.cs`

```csharp
namespace Kartova.SharedKernel.AspNetCore;

internal static class Placeholder { }
```

- [x] **Step 3: Create `Kartova.SharedKernel.Wolverine.csproj`**

File: `src/Kartova.SharedKernel.Wolverine/Kartova.SharedKernel.Wolverine.csproj`

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
    <PackageReference Include="WolverineFx" Version="5.32.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
    <ProjectReference Include="..\Kartova.SharedKernel.Postgres\Kartova.SharedKernel.Postgres.csproj" />
  </ItemGroup>

</Project>
```

File: `src/Kartova.SharedKernel.Wolverine/Placeholder.cs`

```csharp
namespace Kartova.SharedKernel.Wolverine;

internal static class Placeholder { }
```

- [x] **Step 4: Create `Kartova.Testing.Auth.csproj`**

File: `tests/Kartova.Testing.Auth/Kartova.Testing.Auth.csproj`

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
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
    <PackageReference Include="Microsoft.IdentityModel.Tokens" Version="8.3.0" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.3.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

File: `tests/Kartova.Testing.Auth/Placeholder.cs`

```csharp
namespace Kartova.Testing.Auth;

internal static class Placeholder { }
```

- [x] **Step 5: Add all four projects to `Kartova.slnx`**

Replace the `Kartova.slnx` file contents with:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/Kartova.Api/Kartova.Api.csproj" />
    <Project Path="src/Kartova.Migrator/Kartova.Migrator.csproj" />
    <Project Path="src/Kartova.SharedKernel/Kartova.SharedKernel.csproj" />
    <Project Path="src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj" />
    <Project Path="src/Kartova.SharedKernel.Postgres/Kartova.SharedKernel.Postgres.csproj" />
    <Project Path="src/Kartova.SharedKernel.Wolverine/Kartova.SharedKernel.Wolverine.csproj" />
  </Folder>
  <Folder Name="/src/Modules/" />
  <Folder Name="/src/Modules/Catalog/">
    <Project Path="src/Modules/Catalog/Kartova.Catalog.Application/Kartova.Catalog.Application.csproj" />
    <Project Path="src/Modules/Catalog/Kartova.Catalog.Contracts/Kartova.Catalog.Contracts.csproj" />
    <Project Path="src/Modules/Catalog/Kartova.Catalog.Domain/Kartova.Catalog.Domain.csproj" />
    <Project Path="src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj" />
    <Project Path="src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj" />
    <Project Path="src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj" />
    <Project Path="tests/Kartova.Testing.Auth/Kartova.Testing.Auth.csproj" />
  </Folder>
</Solution>
```

- [x] **Step 6: Build to verify project references resolve**

Run: `cmd //c dotnet build Kartova.slnx --nologo`
Expected: Build succeeded. 0 Errors.

- [x] **Step 7: Commit**

```bash
git add src/Kartova.SharedKernel.Postgres/ src/Kartova.SharedKernel.AspNetCore/ src/Kartova.SharedKernel.Wolverine/ tests/Kartova.Testing.Auth/ Kartova.slnx
git commit -m "feat(scaffold): add SharedKernel.Postgres/AspNetCore/Wolverine + Testing.Auth csprojs"
```

---

## Task 6: SharedKernel — multitenancy abstractions

**Goal:** Define the pure (tech-agnostic) abstractions: `TenantId`, `ITenantContext`, `ITenantScope`, `IAsyncTenantScopeHandle`, `ITenantOwned`, and a simple `TenantContextAccessor` holder.

**Files:**
- Create: `src/Kartova.SharedKernel/Multitenancy/TenantId.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/ITenantContext.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/TenantContextAccessor.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/ITenantScope.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/IAsyncTenantScopeHandle.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/ITenantOwned.cs`

- [x] **Step 1: Create `TenantId`**

File: `src/Kartova.SharedKernel/Multitenancy/TenantId.cs`

```csharp
namespace Kartova.SharedKernel.Multitenancy;

public readonly record struct TenantId(Guid Value)
{
    public static readonly TenantId Empty = new(Guid.Empty);

    public static TenantId Parse(string s) =>
        new(Guid.Parse(s));

    public static bool TryParse(string? s, out TenantId tenantId)
    {
        if (Guid.TryParse(s, out var g))
        {
            tenantId = new TenantId(g);
            return true;
        }
        tenantId = Empty;
        return false;
    }

    public override string ToString() => Value.ToString();
}
```

- [x] **Step 2: Create `ITenantContext`**

File: `src/Kartova.SharedKernel/Multitenancy/ITenantContext.cs`

```csharp
namespace Kartova.SharedKernel.Multitenancy;

public interface ITenantContext
{
    TenantId Id { get; }
    bool IsTenantScoped { get; }
    IReadOnlyCollection<string> Roles { get; }

    void Populate(TenantId id, IReadOnlyCollection<string> roles);
    void Clear();
}
```

- [x] **Step 3: Create `TenantContextAccessor`**

File: `src/Kartova.SharedKernel/Multitenancy/TenantContextAccessor.cs`

```csharp
namespace Kartova.SharedKernel.Multitenancy;

public sealed class TenantContextAccessor : ITenantContext
{
    private TenantId _id = TenantId.Empty;
    private IReadOnlyCollection<string> _roles = Array.Empty<string>();
    private bool _populated;

    public TenantId Id => _id;
    public bool IsTenantScoped => _populated && _id != TenantId.Empty;
    public IReadOnlyCollection<string> Roles => _roles;

    public void Populate(TenantId id, IReadOnlyCollection<string> roles)
    {
        _id = id;
        _roles = roles ?? Array.Empty<string>();
        _populated = true;
    }

    public void Clear()
    {
        _id = TenantId.Empty;
        _roles = Array.Empty<string>();
        _populated = false;
    }
}
```

- [x] **Step 4: Create `ITenantScope`**

File: `src/Kartova.SharedKernel/Multitenancy/ITenantScope.cs`

```csharp
namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Owns one physical connection + one transaction per request, with
/// <c>app.current_tenant_id</c> set via <c>SET LOCAL</c>.
/// Only transport adapters (HTTP endpoint filter, Wolverine/Kafka middleware)
/// call <see cref="BeginAsync"/>. Handlers never touch this directly;
/// they just use DbContexts registered via <c>AddModuleDbContext{T}</c>.
/// See ADR-0090.
/// </summary>
public interface ITenantScope
{
    Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct);
    bool IsActive { get; }
}
```

- [x] **Step 5: Create `IAsyncTenantScopeHandle`**

File: `src/Kartova.SharedKernel/Multitenancy/IAsyncTenantScopeHandle.cs`

```csharp
namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Handle returned by <see cref="ITenantScope.BeginAsync"/>.
/// <see cref="CommitAsync"/> must be called to persist work;
/// <see cref="IAsyncDisposable.DisposeAsync"/> rolls back if commit wasn't reached.
/// </summary>
public interface IAsyncTenantScopeHandle : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
```

- [x] **Step 6: Create `ITenantOwned` marker**

File: `src/Kartova.SharedKernel/Multitenancy/ITenantOwned.cs`

```csharp
namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Marker for entities that are tenant-scoped. Architecture tests verify
/// every such entity has an RLS policy in a migration.
/// </summary>
public interface ITenantOwned
{
    TenantId TenantId { get; }
}
```

- [x] **Step 7: Build**

Run: `cmd //c dotnet build src/Kartova.SharedKernel/Kartova.SharedKernel.csproj --nologo`
Expected: Build succeeded. 0 Errors.

- [x] **Step 8: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/
git commit -m "feat(sharedkernel): add multitenancy abstractions (TenantId, ITenantContext, ITenantScope)"
```

---

## Task 7: SharedKernel.Postgres — TenantScope implementation

**Goal:** Implement `TenantScope` owning `NpgsqlConnection` + `IDbContextTransaction`, with `SET LOCAL app.current_tenant_id` issued inside the tx. Delete the placeholder.

**Files:**
- Create: `src/Kartova.SharedKernel.Postgres/TenantScope.cs`
- Delete: `src/Kartova.SharedKernel.Postgres/Placeholder.cs`

- [ ] **Step 1: Delete placeholder**

Run: `cmd //c del src\Kartova.SharedKernel.Postgres\Placeholder.cs`

- [ ] **Step 2: Create `TenantScope`**

File: `src/Kartova.SharedKernel.Postgres/TenantScope.cs`

```csharp
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// ADR-0090 implementation. Scoped DI; one per request.
/// </summary>
public sealed class TenantScope : ITenantScope
{
    private readonly NpgsqlDataSource _dataSource;
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private bool _committed;

    public TenantScope(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public bool IsActive => _connection is not null && _transaction is not null;

    public NpgsqlConnection Connection =>
        _connection ?? throw new InvalidOperationException(
            "TenantScope is not active. BeginAsync must be called by the transport adapter before any DbContext is used.");

    public NpgsqlTransaction Transaction =>
        _transaction ?? throw new InvalidOperationException(
            "TenantScope has no active transaction.");

    public async Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct)
    {
        if (_connection is not null)
        {
            throw new InvalidOperationException("TenantScope already begun for this request.");
        }

        _connection = await _dataSource.OpenConnectionAsync(ct);
        _transaction = await _connection.BeginTransactionAsync(ct);

        await using var cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = "SET LOCAL app.current_tenant_id = $1";
        cmd.Parameters.AddWithValue(id.Value.ToString());
        await cmd.ExecuteNonQueryAsync(ct);

        return new Handle(this);
    }

    private async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("Cannot commit — scope not active.");
        }
        await _transaction.CommitAsync(ct);
        _committed = true;
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (_transaction is not null)
        {
            if (!_committed)
            {
                try { await _transaction.RollbackAsync(); } catch { /* connection may be broken */ }
            }
            await _transaction.DisposeAsync();
            _transaction = null;
        }
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private sealed class Handle : IAsyncTenantScopeHandle
    {
        private readonly TenantScope _scope;
        private bool _disposed;

        public Handle(TenantScope scope) => _scope = scope;

        public Task CommitAsync(CancellationToken ct) => _scope.CommitAsync(ct);

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _scope.DisposeAsyncCore();
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `cmd //c dotnet build src/Kartova.SharedKernel.Postgres/Kartova.SharedKernel.Postgres.csproj --nologo`
Expected: Build succeeded. 0 Errors.

- [ ] **Step 4: Commit**

```bash
git add src/Kartova.SharedKernel.Postgres/
git commit -m "feat(sharedkernel.postgres): TenantScope with transaction-bound SET LOCAL (ADR-0090)"
```

---

## Task 8: SharedKernel.Postgres — interceptors and `AddModuleDbContext<T>`

**Goal:** Add the DbContext initializer interceptor that enlists every module DbContext in the scope's transaction; add the SaveChangesInterceptor that fails fast if a write happens without an active scope; add the `AddModuleDbContext<T>` DI helper.

**Files:**
- Create: `src/Kartova.SharedKernel.Postgres/EnlistInTenantScopeInterceptor.cs`
- Create: `src/Kartova.SharedKernel.Postgres/TenantScopeRequiredInterceptor.cs`
- Create: `src/Kartova.SharedKernel.Postgres/AddModuleDbContextExtensions.cs`

- [ ] **Step 1: Create `EnlistInTenantScopeInterceptor`**

File: `src/Kartova.SharedKernel.Postgres/EnlistInTenantScopeInterceptor.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// Enlists a DbContext into the ambient TenantScope's transaction when the context is initializing.
/// Resolved as scoped because it holds a reference to the scoped TenantScope.
/// </summary>
public sealed class EnlistInTenantScopeInterceptor : IDbContextOptionsExtensionWithDebugInfo, IInterceptor
{
    // Implemented as a simple IDbContextOptionsExtension to ensure per-scope resolution via DI.
    // The real interceptor logic is in SavingChangesInterceptor below.
    public void ApplyServices(IServiceCollection services) { }
    public void Validate(IDbContextOptions options) { }
    public long GetServiceProviderHashCode() => 0;
    public bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => true;
    public DbContextOptionsExtensionInfo Info => throw new NotImplementedException();
}
```

Wait — a simpler, correct approach is to use `DbContextOptionsBuilder.AddInterceptors` with a scoped `IDbTransactionInterceptor` that does the enlistment on first use. Replace the file above with the following **corrected** implementation using `IDbConnectionInterceptor` for the enlistment hook.

Replace `src/Kartova.SharedKernel.Postgres/EnlistInTenantScopeInterceptor.cs` with:

```csharp
using System.Data.Common;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// On the first DbContext operation in a request, enlist the DbContext into the scope's
/// transaction by swapping its connection + UseTransaction. Called exactly once per DbContext
/// instance via the <see cref="IDbConnectionInterceptor.ConnectionOpeningAsync"/> hook.
/// </summary>
public sealed class EnlistInTenantScopeInterceptor : DbConnectionInterceptor
{
    private readonly ITenantScope _scope;
    private bool _enlisted;

    public EnlistInTenantScopeInterceptor(ITenantScope scope)
    {
        _scope = scope;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        return InterceptionResult.Suppress();
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken ct = default)
    {
        // Suppress EF's default Open() — we'll use the scope's already-open connection.
        return ValueTask.FromResult(InterceptionResult.Suppress());
    }
}
```

Note: the suppression-based approach alone isn't sufficient because EF needs a connection to execute commands. The production pattern instead uses `AddModuleDbContext<T>` to configure `options.UseNpgsql(scope.Connection)` at construction time. The interceptor above is unused in the final plan; delete it after confirming `AddModuleDbContext` implementation works (see Step 3). Write it as above for now so the file compiles; it will be removed in Task 9 after tests prove redundancy.

Actually — simplify: keep only the `TenantScopeRequiredInterceptor` for fail-fast on SaveChanges, and do the enlistment in the `AddModuleDbContext` factory delegate (Step 3 below). Delete the `EnlistInTenantScopeInterceptor` file.

Run: `cmd //c del src\Kartova.SharedKernel.Postgres\EnlistInTenantScopeInterceptor.cs`

- [ ] **Step 2: Create `TenantScopeRequiredInterceptor`**

File: `src/Kartova.SharedKernel.Postgres/TenantScopeRequiredInterceptor.cs`

```csharp
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// Fail-fast SaveChanges interceptor: if a tenant-scoped DbContext tries to persist changes
/// and the ambient ITenantScope is not active, throw. Catches "new endpoint added without
/// the tenant-scope filter" during first integration test run.
/// </summary>
public sealed class TenantScopeRequiredInterceptor : SaveChangesInterceptor
{
    private readonly ITenantScope _scope;

    public TenantScopeRequiredInterceptor(ITenantScope scope)
    {
        _scope = scope;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AssertScopeActive();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AssertScopeActive();
        return ValueTask.FromResult(result);
    }

    private void AssertScopeActive()
    {
        if (!_scope.IsActive)
        {
            throw new InvalidOperationException(
                "Attempted to SaveChanges on a tenant-scoped DbContext without an active ITenantScope. "
                + "Either the endpoint is missing TenantScopeEndpointFilter / RequireTenantScope(), "
                + "or the handler is running outside a transport adapter. See ADR-0090.");
        }
    }
}
```

- [ ] **Step 3: Create `AddModuleDbContextExtensions`**

File: `src/Kartova.SharedKernel.Postgres/AddModuleDbContextExtensions.cs`

```csharp
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// DI extension for registering module DbContexts that participate in the per-request tenant scope.
/// Every tenant-scoped module DbContext MUST be registered via this helper, not <see cref="EntityFrameworkServiceCollectionExtensions.AddDbContext{TContext}(IServiceCollection,System.Action{DbContextOptionsBuilder}?,ServiceLifetime,ServiceLifetime)"/>.
/// </summary>
public static class AddModuleDbContextExtensions
{
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var scope = (TenantScope)sp.GetRequiredService<ITenantScope>();

            // Use the scope's already-open connection so all module DbContexts in this request
            // share the same connection + transaction per ADR-0090.
            options.UseNpgsql(scope.Connection);

            // Fail-fast on SaveChanges if scope is not active.
            options.AddInterceptors(sp.GetRequiredService<TenantScopeRequiredInterceptor>());

            configure?.Invoke(options);
        });

        return services;
    }
}
```

Note: resolving `scope.Connection` before `BeginAsync` has been called will throw. This is by design — the transport filter must call `Begin` before any DbContext is resolved in the handler. Integration tests verify both happy path and fail-fast path.

- [ ] **Step 4: Register interceptor service registration helper**

Append inside the same `AddModuleDbContextExtensions.cs` file (end of class):

```csharp
    /// <summary>
    /// Registers the scope + required interceptor services. Call once during composition-root wiring.
    /// </summary>
    public static IServiceCollection AddTenantScope(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext, TenantContextAccessor>();
        services.AddScoped<ITenantScope, TenantScope>();
        services.AddScoped<TenantScopeRequiredInterceptor>();
        return services;
    }
```

(Place before the closing `}` of the class so it lives as another static extension method.)

- [ ] **Step 5: Build**

Run: `cmd //c dotnet build src/Kartova.SharedKernel.Postgres/Kartova.SharedKernel.Postgres.csproj --nologo`
Expected: Build succeeded. 0 Errors.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel.Postgres/
git commit -m "feat(sharedkernel.postgres): AddModuleDbContext helper + SaveChanges fail-fast interceptor"
```

---

## Task 9: SharedKernel.AspNetCore — ProblemTypes + JwtAuthentication + ClaimsTransformation

**Goal:** ADR-0091 problem-type registry; `AddKartovaJwtAuth` wrapper around `AddJwtBearer`; `IClaimsTransformation` that reads `tenant_id` + realm roles and populates the scoped `ITenantContext`.

**Files:**
- Delete: `src/Kartova.SharedKernel.AspNetCore/Placeholder.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/TenantClaimsTransformation.cs`

- [ ] **Step 1: Delete placeholder**

Run: `cmd //c del src\Kartova.SharedKernel.AspNetCore\Placeholder.cs`

- [ ] **Step 2: Create `ProblemTypes`**

File: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`

```csharp
namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Canonical problem-type URI slugs per ADR-0091.
/// URIs resolve to docs pages at https://kartova.io/problems/&lt;slug&gt; (published in a later phase).
/// </summary>
public static class ProblemTypes
{
    private const string Base = "https://kartova.io/problems/";

    public const string InvalidToken           = Base + "invalid-token";
    public const string MissingTenantClaim     = Base + "missing-tenant-claim";
    public const string Forbidden              = Base + "forbidden";
    public const string ResourceNotFound       = Base + "resource-not-found";
    public const string ServiceUnavailable     = Base + "service-unavailable";
    public const string InternalServerError    = Base + "internal-server-error";
    public const string TenantScopeRequired    = Base + "tenant-scope-required";
    public const string ValidationFailed       = Base + "validation-failed";
}
```

- [ ] **Step 3: Create `JwtAuthenticationExtensions`**

File: `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs`

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Wires JwtBearer against KeyCloak using configuration section "Authentication":
    /// <list type="bullet">
    ///  <item><c>Authority</c> — OIDC issuer, e.g. http://keycloak:8080/realms/kartova</item>
    ///  <item><c>MetadataAddress</c> — discovery document URL (optional, derived from Authority if absent)</item>
    ///  <item><c>Audience</c> — expected <c>aud</c> claim, typically client id</item>
    ///  <item><c>RequireHttpsMetadata</c> — true in prod, false in dev docker-compose</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddKartovaJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Authentication:Authority"]
            ?? throw new InvalidOperationException("Authentication:Authority not configured");
        var audience = configuration["Authentication:Audience"]
            ?? throw new InvalidOperationException("Authentication:Audience not configured");
        var metadataAddress = configuration["Authentication:MetadataAddress"];
        var requireHttps = configuration.GetValue("Authentication:RequireHttpsMetadata", defaultValue: true);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                if (!string.IsNullOrWhiteSpace(metadataAddress))
                {
                    options.MetadataAddress = metadataAddress;
                }
                options.Audience = audience;
                options.RequireHttpsMetadata = requireHttps;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.MapInboundClaims = false; // keep raw JWT claim names
            });

        services.AddAuthorization();

        return services;
    }
}
```

- [ ] **Step 4: Create `TenantClaimsTransformation`**

File: `src/Kartova.SharedKernel.AspNetCore/TenantClaimsTransformation.cs`

```csharp
using System.Security.Claims;
using System.Text.Json;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// IClaimsTransformation that reads <c>tenant_id</c> and realm roles from the validated JWT
/// and populates the scoped <see cref="ITenantContext"/>.
/// Realm roles live in the JSON claim <c>realm_access</c> with shape <c>{"roles": [...]}</c>.
/// </summary>
public sealed class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly IServiceProvider _services;

    public TenantClaimsTransformation(IServiceProvider services)
    {
        _services = services;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(principal!);
        }

        var context = _services.GetRequiredService<ITenantContext>();
        context.Clear();

        var tenantIdClaim = principal.FindFirst("tenant_id")?.Value;
        var tenantId = TenantId.Empty;
        if (TenantId.TryParse(tenantIdClaim, out var parsed))
        {
            tenantId = parsed;
        }

        var roles = ExtractRealmRoles(principal);
        context.Populate(tenantId, roles);

        // Also surface realm roles as standard role claims so [Authorize(Roles = "X")] works.
        if (roles.Count > 0 && principal.Identity is ClaimsIdentity id)
        {
            foreach (var role in roles)
            {
                if (!id.HasClaim(ClaimTypes.Role, role))
                {
                    id.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }
        }

        return Task.FromResult(principal);
    }

    private static IReadOnlyCollection<string> ExtractRealmRoles(ClaimsPrincipal principal)
    {
        var realmAccess = principal.FindFirst("realm_access")?.Value;
        if (string.IsNullOrWhiteSpace(realmAccess))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(realmAccess);
            if (doc.RootElement.TryGetProperty("roles", out var rolesElement) &&
                rolesElement.ValueKind == JsonValueKind.Array)
            {
                var result = new List<string>(rolesElement.GetArrayLength());
                foreach (var r in rolesElement.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.String)
                    {
                        var v = r.GetString();
                        if (!string.IsNullOrWhiteSpace(v)) result.Add(v);
                    }
                }
                return result;
            }
        }
        catch (JsonException) { /* malformed claim — ignore */ }

        return Array.Empty<string>();
    }
}
```

- [ ] **Step 5: Build**

Run: `cmd //c dotnet build src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj --nologo`
Expected: Build succeeded. 0 Errors.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore/
git commit -m "feat(sharedkernel.aspnetcore): ProblemTypes + AddKartovaJwtAuth + TenantClaimsTransformation"
```

---

## Task 10: SharedKernel.AspNetCore — endpoint filter + route extensions

**Goal:** `TenantScopeEndpointFilter` — calls `scope.BeginAsync(tenantContext.Id)` before handler, `CommitAsync` after success (before response flush), dispose on exception. `RequireTenantScope()` route extension for declarative wiring.

**Files:**
- Create: `src/Kartova.SharedKernel.AspNetCore/TenantScopeEndpointFilter.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/TenantScopeRouteExtensions.cs`

- [ ] **Step 1: Create `TenantScopeEndpointFilter`**

File: `src/Kartova.SharedKernel.AspNetCore/TenantScopeEndpointFilter.cs`

```csharp
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Wraps tenant-scoped endpoints in an <see cref="ITenantScope"/> lifetime.
/// Commits before ASP.NET writes the response body — commit failures surface as 500.
/// Rolls back on any exception and on un-committed dispose.
/// See ADR-0090.
/// </summary>
public sealed class TenantScopeEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var ct = context.HttpContext.RequestAborted;
        var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        var scope = context.HttpContext.RequestServices.GetRequiredService<ITenantScope>();

        if (!tenantContext.IsTenantScoped)
        {
            return Results.Problem(
                type: ProblemTypes.MissingTenantClaim,
                title: "JWT is missing the tenant_id claim",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        await using var handle = await scope.BeginAsync(tenantContext.Id, ct);
        var result = await next(context);
        await handle.CommitAsync(ct);
        return result;
    }
}
```

- [ ] **Step 2: Create `TenantScopeRouteExtensions`**

File: `src/Kartova.SharedKernel.AspNetCore/TenantScopeRouteExtensions.cs`

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

public static class TenantScopeRouteExtensions
{
    /// <summary>
    /// Attach this to a MapGroup to require authentication AND a tenant_id claim,
    /// and wrap every endpoint in an ITenantScope. See ADR-0090.
    /// </summary>
    public static TBuilder RequireTenantScope<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter<TenantScopeEndpointFilter>();
        return builder;
    }
}
```

- [ ] **Step 3: Build**

Run: `cmd //c dotnet build src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj --nologo`
Expected: Build succeeded. 0 Errors.

- [ ] **Step 4: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore/
git commit -m "feat(sharedkernel.aspnetcore): TenantScopeEndpointFilter + RequireTenantScope extension"
```

---

## Task 11: SharedKernel.Wolverine — middleware skeleton

**Goal:** Parallel pattern for Wolverine handlers. Registered once; currently no tenant-scoped Wolverine handlers exist, so it's a skeleton exercised by architecture tests + future slices.

**Files:**
- Delete: `src/Kartova.SharedKernel.Wolverine/Placeholder.cs`
- Create: `src/Kartova.SharedKernel.Wolverine/TenantScopeWolverineMiddleware.cs`

- [ ] **Step 1: Delete placeholder**

Run: `cmd //c del src\Kartova.SharedKernel.Wolverine\Placeholder.cs`

- [ ] **Step 2: Create middleware**

File: `src/Kartova.SharedKernel.Wolverine/TenantScopeWolverineMiddleware.cs`

```csharp
using Kartova.SharedKernel.Multitenancy;
using Wolverine;

namespace Kartova.SharedKernel.Wolverine;

/// <summary>
/// Wolverine middleware that mirrors TenantScopeEndpointFilter for message handlers.
/// Populates ITenantContext from the message envelope's "tenant_id" header, begins the scope,
/// commits after handler success, rolls back on exception. See ADR-0090.
/// </summary>
public static class TenantScopeWolverineMiddleware
{
    public const string TenantIdHeader = "tenant_id";

    public static async Task<IAsyncTenantScopeHandle?> BeforeAsync(
        Envelope envelope,
        ITenantContext tenantContext,
        ITenantScope scope,
        CancellationToken ct)
    {
        if (!envelope.Headers.TryGetValue(TenantIdHeader, out var raw) ||
            !TenantId.TryParse(raw, out var id))
        {
            // No tenant header → treat as non-tenant-scoped (e.g. platform-admin messages).
            return null;
        }

        tenantContext.Populate(id, Array.Empty<string>());
        return await scope.BeginAsync(id, ct);
    }

    public static async Task AfterAsync(IAsyncTenantScopeHandle? handle, CancellationToken ct)
    {
        if (handle is not null)
        {
            await handle.CommitAsync(ct);
        }
    }

    public static async Task FinallyAsync(IAsyncTenantScopeHandle? handle)
    {
        if (handle is not null)
        {
            await handle.DisposeAsync();
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `cmd //c dotnet build src/Kartova.SharedKernel.Wolverine/Kartova.SharedKernel.Wolverine.csproj --nologo`
Expected: Build succeeded. 0 Errors.

- [ ] **Step 4: Commit**

```bash
git add src/Kartova.SharedKernel.Wolverine/
git commit -m "feat(sharedkernel.wolverine): TenantScopeWolverineMiddleware skeleton for future message handlers"
```

---

## Task 12: Kartova.Testing.Auth — TestJwtSigner + SeededOrgs

**Goal:** RSA-backed local JWT signer and a test-friendly JwtBearer scheme so integration tests can issue tokens without a live KeyCloak.

**Files:**
- Delete: `tests/Kartova.Testing.Auth/Placeholder.cs`
- Create: `tests/Kartova.Testing.Auth/SeededOrgs.cs`
- Create: `tests/Kartova.Testing.Auth/TestJwtSigner.cs`
- Create: `tests/Kartova.Testing.Auth/TestAuthenticationExtensions.cs`

- [ ] **Step 1: Delete placeholder**

Run: `cmd //c del tests\Kartova.Testing.Auth\Placeholder.cs`

- [ ] **Step 2: Create `SeededOrgs` constants (must match realm JSON)**

File: `tests/Kartova.Testing.Auth/SeededOrgs.cs`

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Testing.Auth;

public static class SeededOrgs
{
    public static readonly TenantId OrgA = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    public static readonly TenantId OrgB = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
}
```

- [ ] **Step 3: Create `TestJwtSigner`**

File: `tests/Kartova.Testing.Auth/TestJwtSigner.cs`

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.IdentityModel.Tokens;

namespace Kartova.Testing.Auth;

public sealed class TestJwtSigner
{
    public const string Issuer = "https://test-issuer.kartova.local";
    public const string Audience = "kartova-api";

    private readonly RSA _rsa;
    private readonly RsaSecurityKey _key;

    public TestJwtSigner()
    {
        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-signing-key" };
    }

    public SecurityKey PublicKey => _key;

    public string IssueForTenant(TenantId tenantId, string[] roles, TimeSpan? lifetime = null, string subject = "test-user")
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime ?? TimeSpan.FromMinutes(15));

        var realmAccess = JsonSerializer.Serialize(new { roles });

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Iss, Issuer),
            new(JwtRegisteredClaimNames.Aud, Audience),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("tenant_id", tenantId.Value.ToString()),
            new("realm_access", realmAccess, JsonClaimValueTypes.Json),
        };

        var creds = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string IssueForPlatformAdmin(string[]? extraRoles = null, string subject = "platform-admin-user")
    {
        var roles = new[] { "platform-admin" }.Concat(extraRoles ?? Array.Empty<string>()).ToArray();
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(15);
        var realmAccess = JsonSerializer.Serialize(new { roles });

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Iss, Issuer),
            new(JwtRegisteredClaimNames.Aud, Audience),
            new("realm_access", realmAccess, JsonClaimValueTypes.Json),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string IssueExpired(TenantId tenantId)
    {
        var now = DateTime.UtcNow.AddMinutes(-30);
        var expires = now.AddMinutes(15); // still in the past

        var realmAccess = JsonSerializer.Serialize(new { roles = new[] { "OrgAdmin" } });

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user"),
            new("tenant_id", tenantId.Value.ToString()),
            new("realm_access", realmAccess, JsonClaimValueTypes.Json),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 4: Create `TestAuthenticationExtensions`**

File: `tests/Kartova.Testing.Auth/TestAuthenticationExtensions.cs`

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kartova.Testing.Auth;

public static class TestAuthenticationExtensions
{
    /// <summary>
    /// Replaces the real JWT bearer validation with one that trusts the given TestJwtSigner's
    /// public key. Use in integration-test WebApplicationFactory setup.
    /// </summary>
    public static IServiceCollection UseTestJwtSigner(this IServiceCollection services, TestJwtSigner signer)
    {
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
        {
            opts.Authority = null;
            opts.MetadataAddress = null;
            opts.RequireHttpsMetadata = false;
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = TestJwtSigner.Issuer,
                ValidateAudience = true,
                ValidAudience = TestJwtSigner.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signer.PublicKey,
                ClockSkew = TimeSpan.FromSeconds(5),
            };
            opts.MapInboundClaims = false;
        });
        return services;
    }
}
```

- [ ] **Step 5: Build**

Run: `cmd //c dotnet build tests/Kartova.Testing.Auth/Kartova.Testing.Auth.csproj --nologo`
Expected: Build succeeded. 0 Errors.

- [ ] **Step 6: Commit**

```bash
git add tests/Kartova.Testing.Auth/
git commit -m "feat(testing.auth): RSA test JWT signer + test-only JwtBearer override"
```

---

## Task 13: Organization module — Domain + Contracts csprojs

**Goal:** Create Domain and Contracts projects for the Organization module following the Slice-1 Catalog pattern.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Domain/Kartova.Organization.Domain.csproj`
- Create: `src/Modules/Organization/Kartova.Organization.Domain/OrganizationId.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Domain/Organization.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Contracts/Kartova.Organization.Contracts.csproj`
- Create: `src/Modules/Organization/Kartova.Organization.Contracts/OrganizationDto.cs`

- [ ] **Step 1: Domain csproj**

File: `src/Modules/Organization/Kartova.Organization.Domain/Kartova.Organization.Domain.csproj`

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

- [ ] **Step 2: `OrganizationId`**

File: `src/Modules/Organization/Kartova.Organization.Domain/OrganizationId.cs`

```csharp
namespace Kartova.Organization.Domain;

public readonly record struct OrganizationId(Guid Value)
{
    public static OrganizationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
```

- [ ] **Step 3: `Organization` aggregate**

File: `src/Modules/Organization/Kartova.Organization.Domain/Organization.cs`

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain;

public sealed class Organization : ITenantOwned
{
    public OrganizationId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Organization(OrganizationId id, TenantId tenantId, string name, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        CreatedAt = createdAt;
    }

    // EF constructor
    private Organization() { Name = string.Empty; }

    public static Organization Create(string name)
    {
        ValidateName(name);
        var id = OrganizationId.New();
        // Per ADR-0011, one org = one tenant; tenant_id is the same GUID as the org id.
        var tenantId = new TenantId(id.Value);
        return new Organization(id, tenantId, name, DateTimeOffset.UtcNow);
    }

    public void Rename(string newName)
    {
        ValidateName(newName);
        Name = newName;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name must not be empty.", nameof(name));
        }
        if (name.Length > 100)
        {
            throw new ArgumentException("Organization name must be <= 100 characters.", nameof(name));
        }
    }
}
```

- [ ] **Step 4: Contracts csproj**

File: `src/Modules/Organization/Kartova.Organization.Contracts/Kartova.Organization.Contracts.csproj`

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

- [ ] **Step 5: `OrganizationDto`**

File: `src/Modules/Organization/Kartova.Organization.Contracts/OrganizationDto.cs`

```csharp
namespace Kartova.Organization.Contracts;

public sealed record OrganizationDto(Guid Id, Guid TenantId, string Name, DateTimeOffset CreatedAt);
```

- [ ] **Step 6: Build**

Run: `cmd //c dotnet build src/Modules/Organization/Kartova.Organization.Domain/Kartova.Organization.Domain.csproj src/Modules/Organization/Kartova.Organization.Contracts/Kartova.Organization.Contracts.csproj --nologo`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Domain/ src/Modules/Organization/Kartova.Organization.Contracts/
git commit -m "feat(organization): Domain + Contracts (aggregate, DTO)"
```

---

## Task 14: Organization module — Application csproj

**Goal:** Application layer with a simple `IOrganizationQueries` service for `GET /organizations/me`. Wolverine handler wiring deferred (not needed for a query endpoint in Slice 2; minimal APIs can call the service directly).

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Application/Kartova.Organization.Application.csproj`
- Create: `src/Modules/Organization/Kartova.Organization.Application/IOrganizationQueries.cs`

- [ ] **Step 1: Application csproj**

File: `src/Modules/Organization/Kartova.Organization.Application/Kartova.Organization.Application.csproj`

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
    <ProjectReference Include="..\Kartova.Organization.Domain\Kartova.Organization.Domain.csproj" />
    <ProjectReference Include="..\Kartova.Organization.Contracts\Kartova.Organization.Contracts.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `IOrganizationQueries`**

File: `src/Modules/Organization/Kartova.Organization.Application/IOrganizationQueries.cs`

```csharp
using Kartova.Organization.Contracts;

namespace Kartova.Organization.Application;

public interface IOrganizationQueries
{
    Task<OrganizationDto?> GetCurrentAsync(CancellationToken ct);
}
```

- [ ] **Step 3: Build**

Run: `cmd //c dotnet build src/Modules/Organization/Kartova.Organization.Application/Kartova.Organization.Application.csproj --nologo`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Application/
git commit -m "feat(organization): Application layer (IOrganizationQueries)"
```

---

## Task 15: Organization module — Infrastructure csproj + DbContext + migration

**Goal:** EF Core DbContext with RLS-enforced `organizations` table, initial migration including raw SQL for RLS policy + FORCE, and a query implementation.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationDbContext.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEntityTypeConfiguration.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationQueries.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/20260422120000_InitialOrganization.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/20260422120000_InitialOrganization.Designer.cs` (autogen in practice; include stub)
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/OrganizationDbContextModelSnapshot.cs` (autogen stub)

Implementer note: the `.Designer.cs` and `ModelSnapshot.cs` will be fully regenerated by `dotnet ef migrations add` — do not hand-author. Step 5 below uses the EF CLI; the contents here are a guide.

- [ ] **Step 1: Infrastructure csproj**

File: `src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj`

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
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageReference Include="System.Security.Cryptography.Xml" Version="10.0.7" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.Organization.Domain\Kartova.Organization.Domain.csproj" />
    <ProjectReference Include="..\Kartova.Organization.Application\Kartova.Organization.Application.csproj" />
    <ProjectReference Include="..\Kartova.Organization.Contracts\Kartova.Organization.Contracts.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel.Postgres\Kartova.SharedKernel.Postgres.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `OrganizationDbContext`**

File: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationDbContext.cs`

```csharp
using Kartova.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class OrganizationDbContext : DbContext
{
    public OrganizationDbContext(DbContextOptions<OrganizationDbContext> options) : base(options) { }

    public DbSet<Organization.Domain.Organization> Organizations => Set<Organization.Domain.Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}
```

- [ ] **Step 3: `OrganizationEntityTypeConfiguration`**

File: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEntityTypeConfiguration.cs`

```csharp
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Organization.Infrastructure;

internal sealed class OrganizationEntityTypeConfiguration : IEntityTypeConfiguration<Domain.Organization>
{
    public void Configure(EntityTypeBuilder<Domain.Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, g => new OrganizationId(g));

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(t => t.Value, g => new TenantId(g));

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(x => x.TenantId).HasDatabaseName("idx_organizations_tenant");

        // Defense-in-depth per ADR-0012: app-layer filter paired with DB-level RLS policy.
        // The tenant id for the filter is the *connection-level* GUC; we can't read it from EF,
        // so we instead rely on RLS + an explicit query where callers want strict id-matching.
        // No global query filter here because RLS already enforces tenant isolation server-side.
    }
}
```

- [ ] **Step 4: `OrganizationQueries`**

File: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationQueries.cs`

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

internal sealed class OrganizationQueries : IOrganizationQueries
{
    private readonly OrganizationDbContext _db;

    public OrganizationQueries(OrganizationDbContext db)
    {
        _db = db;
    }

    public async Task<OrganizationDto?> GetCurrentAsync(CancellationToken ct)
    {
        // RLS filters rows to the current tenant. Expect 0 or 1 row for the current tenant.
        var row = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(ct);
        if (row is null) return null;
        return new OrganizationDto(row.Id.Value, row.TenantId.Value, row.Name, row.CreatedAt);
    }
}
```

- [ ] **Step 5: Generate the initial migration via EF CLI**

Run these commands from repo root:

```bash
cmd //c dotnet ef migrations add InitialOrganization ^
  --project src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj ^
  --startup-project src/Kartova.Migrator/Kartova.Migrator.csproj ^
  --context OrganizationDbContext ^
  --output-dir Migrations
```

Expected: creates `Migrations/<timestamp>_InitialOrganization.cs`, `.Designer.cs`, and `OrganizationDbContextModelSnapshot.cs`.

If the startup-project doesn't know about `OrganizationDbContext` yet, temporarily add a `ProjectReference` from `Kartova.Migrator` to `Kartova.Organization.Infrastructure` before running this command (it will be added permanently in Task 18 anyway — do it now).

Run:
```bash
cmd //c dotnet add src/Kartova.Migrator/Kartova.Migrator.csproj reference src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj
```

Then re-run the `dotnet ef migrations add` command above.

- [ ] **Step 6: Add RLS SQL to the generated `Up` method**

Open the generated `<timestamp>_InitialOrganization.cs`. Locate the `Up(MigrationBuilder migrationBuilder)` method and, **at the end of the body** (after any `CreateTable` / `CreateIndex` calls), append:

```csharp
            migrationBuilder.Sql(@"
ALTER TABLE organizations ENABLE ROW LEVEL SECURITY;
ALTER TABLE organizations FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON organizations
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
```

In the `Down(MigrationBuilder migrationBuilder)` method, **at the start of the body** (before `DropTable` / `DropIndex` calls), prepend:

```csharp
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON organizations;
ALTER TABLE organizations DISABLE ROW LEVEL SECURITY;
");
```

- [ ] **Step 7: Build**

Run: `cmd //c dotnet build src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj --nologo`
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/ src/Kartova.Migrator/Kartova.Migrator.csproj
git commit -m "feat(organization): Infrastructure — DbContext, EF config, queries, InitialOrganization migration with RLS"
```

---

## Task 16: Organization.Infrastructure.Admin — BYPASSRLS DbContext + admin query

**Goal:** Separate assembly for the `POST /api/v1/admin/organizations` bypass path. Uses a different connection string (`kartova_bypass_rls` role). Never touches `ITenantScope`.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/Kartova.Organization.Infrastructure.Admin.csproj`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AdminOrganizationDbContext.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/IAdminOrganizationCommands.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AdminOrganizationCommands.cs`

- [ ] **Step 1: Admin csproj**

File: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/Kartova.Organization.Infrastructure.Admin.csproj`

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
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.Organization.Domain\Kartova.Organization.Domain.csproj" />
    <ProjectReference Include="..\Kartova.Organization.Infrastructure\Kartova.Organization.Infrastructure.csproj" />
    <ProjectReference Include="..\Kartova.Organization.Contracts\Kartova.Organization.Contracts.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `AdminOrganizationDbContext`**

File: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AdminOrganizationDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure.Admin;

/// <summary>
/// DbContext for the admin bypass path (POST /api/v1/admin/organizations).
/// Uses a connection string with a BYPASSRLS role, so RLS policies do not filter rows.
/// NOT registered via AddModuleDbContext — does NOT participate in ITenantScope.
/// </summary>
public sealed class AdminOrganizationDbContext : DbContext
{
    public AdminOrganizationDbContext(DbContextOptions<AdminOrganizationDbContext> options) : base(options) { }

    public DbSet<Kartova.Organization.Domain.Organization> Organizations => Set<Kartova.Organization.Domain.Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Reuse the same configuration as the tenant-scoped DbContext.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}
```

- [ ] **Step 3: `IAdminOrganizationCommands`**

File: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/IAdminOrganizationCommands.cs`

```csharp
using Kartova.Organization.Contracts;

namespace Kartova.Organization.Infrastructure.Admin;

public interface IAdminOrganizationCommands
{
    Task<OrganizationDto> CreateAsync(string name, CancellationToken ct);
}
```

- [ ] **Step 4: `AdminOrganizationCommands`**

File: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AdminOrganizationCommands.cs`

```csharp
using Kartova.Organization.Contracts;

namespace Kartova.Organization.Infrastructure.Admin;

internal sealed class AdminOrganizationCommands : IAdminOrganizationCommands
{
    private readonly AdminOrganizationDbContext _db;

    public AdminOrganizationCommands(AdminOrganizationDbContext db)
    {
        _db = db;
    }

    public async Task<OrganizationDto> CreateAsync(string name, CancellationToken ct)
    {
        var org = Kartova.Organization.Domain.Organization.Create(name);
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync(ct);
        return new OrganizationDto(org.Id.Value, org.TenantId.Value, org.Name, org.CreatedAt);
    }
}
```

- [ ] **Step 5: Add project to slnx and build**

Run:
```bash
cmd //c dotnet sln Kartova.slnx add src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/Kartova.Organization.Infrastructure.Admin.csproj
cmd //c dotnet build src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/Kartova.Organization.Infrastructure.Admin.csproj --nologo
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/ Kartova.slnx
git commit -m "feat(organization.admin): AdminOrganizationDbContext for BYPASSRLS admin path"
```

---

## Task 17: Organization module — OrganizationModule `IModule`

**Goal:** Module wiring — implements `IModule`, registers `OrganizationDbContext` via `AddModuleDbContext`, registers `AdminOrganizationDbContext` separately with the BYPASSRLS connection string, registers queries + commands.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs`

- [ ] **Step 1: Create `OrganizationModule`**

File: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs`

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Kartova.Organization.Infrastructure;

public sealed class OrganizationModule : IModule
{
    public string Name => "Organization";

    public Type DbContextType => typeof(OrganizationDbContext);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Tenant-scoped DbContext — connection flows from ITenantScope per ADR-0090.
        services.AddModuleDbContext<OrganizationDbContext>();
        services.AddScoped<IOrganizationQueries, OrganizationQueries>();

        // Admin bypass DbContext — separate connection string with BYPASSRLS role.
        var bypassCs = configuration.GetConnectionString("KartovaBypass")
            ?? throw new InvalidOperationException("ConnectionStrings__KartovaBypass not configured");
        services.AddDbContext<AdminOrganizationDbContext>(options => options.UseNpgsql(bypassCs));
        services.AddScoped<IAdminOrganizationCommands, AdminOrganizationCommands>();
    }

    public void ConfigureWolverine(WolverineOptions options) { }
}
```

If `IModule` in `Kartova.SharedKernel` does not already declare `DbContextType`, check `src/Kartova.SharedKernel/IModule.cs`; adjust this class to match the actual interface contract. (Slice 1 defined it with `Name`, `RegisterServices`, `ConfigureWolverine`; `DbContextType` may not exist — in that case remove the override.)

Run: `cmd //c findstr /s /c:"interface IModule" src\Kartova.SharedKernel\*.cs`
Expected: shows the interface definition; adjust `OrganizationModule` to match exactly.

- [ ] **Step 2: Build**

Run: `cmd //c dotnet build src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj --nologo`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs
git commit -m "feat(organization): OrganizationModule — registers tenant-scoped + admin DbContexts"
```

---

## Task 18: Kartova.Migrator — register Organization module

**Goal:** Extend the migrator's module list to include `OrganizationModule` so `Database.MigrateAsync` runs the InitialOrganization migration on startup.

**Files:**
- Modify: `src/Kartova.Migrator/Program.cs`
- Modify: `src/Kartova.Migrator/Kartova.Migrator.csproj` (project reference already added in Task 15)

- [ ] **Step 1: Inspect current migrator**

Run: `type src\Kartova.Migrator\Program.cs`
Expected: iterates over a `modules` array. Add `new OrganizationModule()` to the array.

- [ ] **Step 2: Edit `Program.cs`**

Open `src/Kartova.Migrator/Program.cs`. Locate the line constructing the modules array (looks like `IModule[] modules = [ new CatalogModule(), ];`). Replace it with:

```csharp
IModule[] modules =
[
    new CatalogModule(),
    new OrganizationModule(),
];
```

Add the using at the top:
```csharp
using Kartova.Organization.Infrastructure;
```

- [ ] **Step 3: Build**

Run: `cmd //c dotnet build src/Kartova.Migrator/Kartova.Migrator.csproj --nologo`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Kartova.Migrator/Program.cs
git commit -m "feat(migrator): register OrganizationModule for Slice 2 migrations"
```

---

## Task 19: Kartova.Api — Program.cs wiring for auth + tenant scope + endpoints

**Goal:** Extend `Kartova.Api/Program.cs` to add `AddKartovaJwtAuth`, `AddProblemDetails`, `AddTenantScope`, `TenantClaimsTransformation`, and the two endpoint groups: tenant-scoped (`/api/v1`) using `.RequireTenantScope()` and admin (`/api/v1/admin`) using `RequireRole("platform-admin")`. Register `NpgsqlDataSource` for use by `TenantScope`.

**Files:**
- Modify: `src/Kartova.Api/Kartova.Api.csproj`
- Modify: `src/Kartova.Api/Program.cs`
- Create: `src/Kartova.Api/Endpoints/OrganizationEndpoints.cs`
- Create: `src/Kartova.Api/Endpoints/AdminOrganizationEndpoints.cs`

- [ ] **Step 1: Update Api csproj**

File: `src/Kartova.Api/Kartova.Api.csproj`

Replace the contents with:

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

  <ItemGroup>
    <PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
    <PackageReference Include="Npgsql.DependencyInjection" Version="10.0.0" />
    <PackageReference Include="WolverineFx" Version="5.32.0" />
    <PackageReference Include="WolverineFx.EntityFrameworkCore" Version="5.32.0" />
    <PackageReference Include="WolverineFx.Postgresql" Version="5.32.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
    <ProjectReference Include="..\Kartova.SharedKernel.AspNetCore\Kartova.SharedKernel.AspNetCore.csproj" />
    <ProjectReference Include="..\Kartova.SharedKernel.Postgres\Kartova.SharedKernel.Postgres.csproj" />
    <ProjectReference Include="..\Kartova.SharedKernel.Wolverine\Kartova.SharedKernel.Wolverine.csproj" />
    <ProjectReference Include="..\Modules\Catalog\Kartova.Catalog.Infrastructure\Kartova.Catalog.Infrastructure.csproj" />
    <ProjectReference Include="..\Modules\Organization\Kartova.Organization.Infrastructure\Kartova.Organization.Infrastructure.csproj" />
    <ProjectReference Include="..\Modules\Organization\Kartova.Organization.Infrastructure.Admin\Kartova.Organization.Infrastructure.Admin.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Kartova.ArchitectureTests" />
    <InternalsVisibleTo Include="Kartova.Api.IntegrationTests" />
    <InternalsVisibleTo Include="Kartova.Organization.IntegrationTests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Rewrite `Program.cs`**

File: `src/Kartova.Api/Program.cs`

```csharp
using System.Reflection;
using JasperFx;
using Kartova.Catalog.Infrastructure;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Postgres;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Wolverine;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

// Module registry.
IModule[] modules =
[
    new CatalogModule(),
    new OrganizationModule(),
];

foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

var kartovaConnection = builder.Configuration.GetConnectionString("Kartova")
    ?? throw new InvalidOperationException("ConnectionStrings__Kartova missing");

// NpgsqlDataSource — used by TenantScope to open pooled connections.
builder.Services.AddNpgsqlDataSource(kartovaConnection);

// Tenant scope + required interceptor — ADR-0090.
builder.Services.AddTenantScope();

// JWT authentication — ADR-0006/0007/0014 + claims transformation populates ITenantContext.
builder.Services.AddKartovaJwtAuth(builder.Configuration);
builder.Services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();

// RFC 7807 problem details — ADR-0091.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
    };
});

// Wolverine — persistence only; no message routing in Slice 2.
builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(kartovaConnection, schemaName: "wolverine");

    foreach (var module in modules)
    {
        module.ConfigureWolverine(opts);
    }
});

// Health checks — ADR-0060.
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(kartovaConnection, name: "postgres", tags: ["ready"]);

var app = builder.Build();

app.UseStatusCodePages();
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// Anonymous version endpoint.
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
}).AllowAnonymous();

// Tenant-scoped routes.
var tenantScoped = app.MapGroup("/api/v1").RequireTenantScope();
Kartova.Api.Endpoints.OrganizationEndpoints.Map(tenantScoped);

// Admin (non-tenant) routes — platform-admin only.
var admin = app.MapGroup("/api/v1/admin").RequireAuthorization(policy => policy.RequireRole("platform-admin"));
Kartova.Api.Endpoints.AdminOrganizationEndpoints.Map(admin);

return await app.RunJasperFxCommands(args);
```

- [ ] **Step 3: Create `OrganizationEndpoints`**

File: `src/Kartova.Api/Endpoints/OrganizationEndpoints.cs`

```csharp
using Kartova.Organization.Application;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Api.Endpoints;

internal static class OrganizationEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/organizations/me", GetMeAsync);

        // Admin role demo endpoint — proves role-based authorization works end-to-end.
        group.MapGet("/organizations/me/admin-only", GetAdminOnlyAsync)
            .RequireAuthorization(policy => policy.RequireRole("OrgAdmin"));
    }

    internal static async Task<IResult> GetMeAsync(IOrganizationQueries queries, CancellationToken ct)
    {
        var org = await queries.GetCurrentAsync(ct);
        if (org is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Organization not found",
                detail: "The current tenant has no visible Organization row.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(org);
    }

    internal static IResult GetAdminOnlyAsync()
    {
        return Results.Ok(new { message = "ok" });
    }
}
```

- [ ] **Step 4: Create `AdminOrganizationEndpoints`**

File: `src/Kartova.Api/Endpoints/AdminOrganizationEndpoints.cs`

```csharp
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Api.Endpoints;

internal static class AdminOrganizationEndpoints
{
    public sealed record CreateOrganizationRequest(string Name);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/organizations", CreateAsync);
    }

    internal static async Task<IResult> CreateAsync(
        CreateOrganizationRequest request,
        IAdminOrganizationCommands commands,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Invalid name",
                detail: "Name must not be empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var org = await commands.CreateAsync(request.Name, ct);
        return Results.Created($"/api/v1/organizations/{org.Id}", org);
    }
}
```

- [ ] **Step 5: Build**

Run: `cmd //c dotnet build src/Kartova.Api/Kartova.Api.csproj --nologo`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.Api/
git commit -m "feat(api): wire JWT auth + tenant scope + problem details + organization endpoints"
```

---

## Task 20: Organization unit tests

**Goal:** Unit tests for `Organization.Create` invariants and `TenantClaimsTransformation` behavior.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj`
- Create: `src/Modules/Organization/Kartova.Organization.Tests/OrganizationAggregateTests.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Tests/TenantClaimsTransformationTests.cs`

- [ ] **Step 1: Tests csproj**

File: `src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj`

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
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="7.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Kartova.Organization.Domain\Kartova.Organization.Domain.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel.AspNetCore\Kartova.SharedKernel.AspNetCore.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `OrganizationAggregateTests`**

File: `src/Modules/Organization/Kartova.Organization.Tests/OrganizationAggregateTests.cs`

```csharp
using FluentAssertions;
using Xunit;

namespace Kartova.Organization.Tests;

public class OrganizationAggregateTests
{
    [Fact]
    public void Create_with_valid_name_sets_tenant_id_equal_to_id()
    {
        var org = Domain.Organization.Create("Acme");

        org.Id.Value.Should().NotBeEmpty();
        org.TenantId.Value.Should().Be(org.Id.Value);
        org.Name.Should().Be("Acme");
        org.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_empty_name_throws(string? name)
    {
        var act = () => Domain.Organization.Create(name!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_with_too_long_name_throws()
    {
        var name = new string('a', 101);
        var act = () => Domain.Organization.Create(name);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_updates_name()
    {
        var org = Domain.Organization.Create("Acme");
        org.Rename("NewName");
        org.Name.Should().Be("NewName");
    }
}
```

- [ ] **Step 3: `TenantClaimsTransformationTests`**

File: `src/Modules/Organization/Kartova.Organization.Tests/TenantClaimsTransformationTests.cs`

```csharp
using System.Security.Claims;
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kartova.Organization.Tests;

public class TenantClaimsTransformationTests
{
    private static (ClaimsPrincipal principal, ITenantContext ctx) Setup(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "test");
        var principal = new ClaimsPrincipal(identity);
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, TenantContextAccessor>();
        var sp = services.BuildServiceProvider();
        return (principal, sp.GetRequiredService<ITenantContext>());
    }

    [Fact]
    public async Task Populates_tenant_id_and_roles_from_JWT_claims()
    {
        var (principal, ctx) = Setup(
            new Claim("tenant_id", "11111111-1111-1111-1111-111111111111"),
            new Claim("realm_access", """{"roles":["OrgAdmin","Member"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        var result = await sut.TransformAsync(principal);

        ctx.IsTenantScoped.Should().BeTrue();
        ctx.Id.Value.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        ctx.Roles.Should().BeEquivalentTo(new[] { "OrgAdmin", "Member" });
        result.IsInRole("OrgAdmin").Should().BeTrue();
    }

    [Fact]
    public async Task Missing_tenant_id_claim_leaves_context_non_tenant_scoped()
    {
        var (principal, ctx) = Setup(
            new Claim("realm_access", """{"roles":["platform-admin"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        await sut.TransformAsync(principal);

        ctx.IsTenantScoped.Should().BeFalse();
        ctx.Roles.Should().BeEquivalentTo(new[] { "platform-admin" });
    }

    [Fact]
    public async Task Invalid_tenant_id_claim_leaves_context_non_tenant_scoped()
    {
        var (principal, ctx) = Setup(new Claim("tenant_id", "not-a-guid"));
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        await sut.TransformAsync(principal);

        ctx.IsTenantScoped.Should().BeFalse();
    }

    [Fact]
    public async Task Unauthenticated_principal_is_returned_unchanged()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type = not authenticated
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, TenantContextAccessor>();
        var sp = services.BuildServiceProvider();
        var sut = new TenantClaimsTransformation(sp);

        var result = await sut.TransformAsync(principal);

        result.Should().BeSameAs(principal);
        sp.GetRequiredService<ITenantContext>().IsTenantScoped.Should().BeFalse();
    }

    private static IServiceProvider ProviderFor(ITenantContext ctx)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        return services.BuildServiceProvider();
    }
}
```

- [ ] **Step 4: Add project to slnx and run tests**

Run:
```bash
cmd //c dotnet sln Kartova.slnx add src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj
cmd //c dotnet test src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj --nologo
```
Expected: All tests pass (~7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Tests/ Kartova.slnx
git commit -m "test(organization): unit tests for aggregate + claims transformation"
```

---

## Task 21: Organization integration tests — fixture + happy path

**Goal:** Set up an integration-test fixture (Testcontainers.PostgreSql + WebApplicationFactory + TestJwtSigner override). One test: happy path GET /organizations/me for Org A.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj`
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFixture.cs`
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/OrganizationEndpointHappyPathTests.cs`

- [ ] **Step 1: Tests csproj**

File: `src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj`

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
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="7.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.0.0" />
    <PackageReference Include="Npgsql" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\src\Kartova.Api\Kartova.Api.csproj" />
    <ProjectReference Include="..\Kartova.Organization.Infrastructure\Kartova.Organization.Infrastructure.csproj" />
    <ProjectReference Include="..\Kartova.Organization.Infrastructure.Admin\Kartova.Organization.Infrastructure.Admin.csproj" />
    <ProjectReference Include="..\..\..\..\tests\Kartova.Testing.Auth\Kartova.Testing.Auth.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `KartovaApiFixture`**

File: `src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFixture.cs`

```csharp
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

public sealed class KartovaApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("kartova")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public TestJwtSigner Signer { get; } = new();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await InitRolesAndSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
    }

    public string MainConnectionString => new NpgsqlConnectionStringBuilder(_pg.GetConnectionString())
    {
        Username = "kartova_app",
        Password = "dev",
    }.ToString();

    public string BypassConnectionString => new NpgsqlConnectionStringBuilder(_pg.GetConnectionString())
    {
        Username = "kartova_bypass_rls",
        Password = "dev_only",
    }.ToString();

    private async Task InitRolesAndSchemaAsync()
    {
        var cs = _pg.GetConnectionString();
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE ROLE migrator WITH LOGIN PASSWORD 'dev' CREATEDB;
            CREATE ROLE kartova_app WITH LOGIN PASSWORD 'dev';
            CREATE ROLE kartova_bypass_rls WITH LOGIN PASSWORD 'dev_only' BYPASSRLS;
            GRANT CONNECT ON DATABASE kartova TO kartova_app, kartova_bypass_rls;
            ALTER SCHEMA public OWNER TO migrator;
            GRANT USAGE, CREATE ON SCHEMA public TO kartova_app;
            GRANT USAGE, CREATE ON SCHEMA public TO kartova_bypass_rls;
            GRANT CREATE ON DATABASE kartova TO kartova_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kartova_app, kartova_bypass_rls;
            ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
                GRANT USAGE, SELECT ON SEQUENCES TO kartova_app, kartova_bypass_rls;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Kartova"] = MigratorConnectionString,
                ["ConnectionStrings:KartovaBypass"] = BypassConnectionString,
                ["Authentication:Authority"] = TestJwtSigner.Issuer,
                ["Authentication:Audience"] = TestJwtSigner.Audience,
                ["Authentication:RequireHttpsMetadata"] = "false",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.UseTestJwtSigner(Signer);
        });
    }

    public string MigratorConnectionString => new NpgsqlConnectionStringBuilder(_pg.GetConnectionString())
    {
        Username = "migrator",
        Password = "dev",
    }.ToString();

    public async Task RunMigrationsAsync()
    {
        // Delegate to the same code path the migrator container uses.
        // For simplicity, run CatalogDbContext + OrganizationDbContext migrations directly.
        using var sp = Services.CreateScope();

        // Apply Catalog migrations.
        var catalogType = Type.GetType("Kartova.Catalog.Infrastructure.CatalogDbContext, Kartova.Catalog.Infrastructure");
        if (catalogType is not null)
        {
            var db = (Microsoft.EntityFrameworkCore.DbContext)sp.ServiceProvider.GetRequiredService(catalogType);
            await db.Database.MigrateAsync();
        }

        var orgDb = sp.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        await orgDb.Database.MigrateAsync();
    }

    public async Task<Guid> SeedOrganizationAsync(Guid tenantId, string name)
    {
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO organizations (id, tenant_id, name, created_at) VALUES ($1, $2, $3, now()) RETURNING id";
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(name);
        var id = (Guid)(await cmd.ExecuteScalarAsync())!;
        return id;
    }
}
```

- [ ] **Step 3: `OrganizationEndpointHappyPathTests`**

File: `src/Modules/Organization/Kartova.Organization.IntegrationTests/OrganizationEndpointHappyPathTests.cs`

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

public class OrganizationEndpointHappyPathTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture _fx;

    public OrganizationEndpointHappyPathTests(KartovaApiFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task Get_me_returns_current_tenant_row()
    {
        await _fx.RunMigrationsAsync();
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgB.Value, "Org B");

        var client = _fx.CreateClient();
        var token = _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/organizations/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<OrganizationDto>();
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(SeededOrgs.OrgA.Value);
        dto.Name.Should().Be("Org A");
    }
}
```

- [ ] **Step 4: Add project to slnx and run the test**

Run:
```bash
cmd //c dotnet sln Kartova.slnx add src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj
cmd //c dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --nologo
```
Expected: Test passes (requires Docker for Testcontainers).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.IntegrationTests/ Kartova.slnx
git commit -m "test(organization): integration fixture + happy-path GET /organizations/me"
```

---

## Task 22: Organization integration tests — cross-tenant isolation + raw SQL proof

**Goal:** Prove RLS actually filters: Org A token + Org B row in DB → 404 from Org A's perspective. Raw BYPASSRLS query shows both rows exist.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantIsolationTests.cs`

- [ ] **Step 1: Create test**

File: `src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantIsolationTests.cs`

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;
using Npgsql;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

public class TenantIsolationTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture _fx;

    public TenantIsolationTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Each_tenant_only_sees_its_own_organization()
    {
        await _fx.RunMigrationsAsync();
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgB.Value, "Org B");

        var client = _fx.CreateClient();

        // Org A
        var tokenA = _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var respA = await client.GetAsync("/api/v1/organizations/me");
        respA.StatusCode.Should().Be(HttpStatusCode.OK);
        (await respA.Content.ReadFromJsonAsync<OrganizationDto>())!.Name.Should().Be("Org A");

        // Org B
        var tokenB = _fx.Signer.IssueForTenant(SeededOrgs.OrgB, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var respB = await client.GetAsync("/api/v1/organizations/me");
        respB.StatusCode.Should().Be(HttpStatusCode.OK);
        (await respB.Content.ReadFromJsonAsync<OrganizationDto>())!.Name.Should().Be("Org B");
    }

    [Fact]
    public async Task Raw_sql_as_bypass_role_sees_both_rows()
    {
        await _fx.RunMigrationsAsync();
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgB.Value, "Org B");

        await using var conn = new NpgsqlConnection(_fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM organizations";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().BeGreaterThanOrEqualTo(2);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `cmd //c dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --filter FullyQualifiedName~TenantIsolationTests --nologo`
Expected: Both tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantIsolationTests.cs
git commit -m "test(organization): tenant isolation + raw SQL BYPASSRLS proof"
```

---

## Task 23: Organization integration tests — auth/role errors and admin bypass path

**Goal:** 401 (no token, invalid, expired, missing tenant_id claim), 403 (wrong role on admin-only endpoint, non-platform-admin on `POST /admin/organizations`), happy path on admin bypass creating new org.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/AuthErrorTests.cs`
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/AdminBypassTests.cs`

- [ ] **Step 1: `AuthErrorTests`**

File: `src/Modules/Organization/Kartova.Organization.IntegrationTests/AuthErrorTests.cs`

```csharp
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

public class AuthErrorTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture _fx;

    public AuthErrorTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task No_token_returns_401()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v1/organizations/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _fx.Signer.IssueExpired(SeededOrgs.OrgA));
        var resp = await client.GetAsync("/api/v1/organizations/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Platform_admin_without_tenant_hits_missing_tenant_on_tenant_scoped_route()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _fx.Signer.IssueForPlatformAdmin());
        var resp = await client.GetAsync("/api/v1/organizations/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("missing-tenant-claim");
    }

    [Fact]
    public async Task Non_org_admin_gets_403_on_admin_only_endpoint()
    {
        await _fx.RunMigrationsAsync();
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "Member" }));
        var resp = await client.GetAsync("/api/v1/organizations/me/admin-only");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 2: `AdminBypassTests`**

File: `src/Modules/Organization/Kartova.Organization.IntegrationTests/AdminBypassTests.cs`

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

public class AdminBypassTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture _fx;

    public AdminBypassTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Platform_admin_can_create_organization_without_tenant_scope()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _fx.Signer.IssueForPlatformAdmin());

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = "Newly created" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await resp.Content.ReadFromJsonAsync<OrganizationDto>();
        dto!.Name.Should().Be("Newly created");
        dto.Id.Should().Be(dto.TenantId);
    }

    [Fact]
    public async Task Non_platform_admin_cannot_post_admin_organizations()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" }));

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = "Denied" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 3: Run**

Run: `cmd //c dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --nologo`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.IntegrationTests/AuthErrorTests.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/AdminBypassTests.cs
git commit -m "test(organization): auth/role error cases + admin bypass happy + denied"
```

---

## Task 24: Architecture tests for Slice 2

**Goal:** Eight new NetArchTest rules and one reflection-based rule. Part of the mandatory CI gate (ADR-0083).

**Files:**
- Create: `tests/Kartova.ArchitectureTests/TenantScopeRules.cs`

Add a project reference first:
```bash
cmd //c dotnet add tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj reference src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj src/Kartova.SharedKernel.Postgres/Kartova.SharedKernel.Postgres.csproj src/Kartova.SharedKernel.Wolverine/Kartova.SharedKernel.Wolverine.csproj src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/Kartova.Organization.Infrastructure.Admin.csproj
```

- [ ] **Step 1: Create `TenantScopeRules`**

File: `tests/Kartova.ArchitectureTests/TenantScopeRules.cs`

```csharp
using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Kartova.ArchitectureTests;

public class TenantScopeRules
{
    private static readonly Assembly SharedKernel = typeof(Kartova.SharedKernel.Multitenancy.ITenantScope).Assembly;
    private static readonly Assembly SharedKernelAspNetCore = typeof(Kartova.SharedKernel.AspNetCore.TenantScopeEndpointFilter).Assembly;
    private static readonly Assembly SharedKernelPostgres = typeof(Kartova.SharedKernel.Postgres.TenantScope).Assembly;
    private static readonly Assembly SharedKernelWolverine = typeof(Kartova.SharedKernel.Wolverine.TenantScopeWolverineMiddleware).Assembly;
    private static readonly Assembly OrganizationInfrastructure = typeof(Kartova.Organization.Infrastructure.OrganizationDbContext).Assembly;
    private static readonly Assembly OrganizationInfrastructureAdmin = typeof(Kartova.Organization.Infrastructure.Admin.AdminOrganizationDbContext).Assembly;

    [Fact]
    public void SharedKernel_has_no_framework_dependencies()
    {
        var forbidden = new[]
        {
            "Npgsql",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "WolverineFx",
            "KafkaFlow",
        };

        var result = Types.InAssembly(SharedKernel)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Kartova.SharedKernel must stay technology-agnostic; tech-specific code lives in SharedKernel.Postgres/AspNetCore/Wolverine (ADR-0090)");
    }

    [Fact]
    public void Admin_bypass_DbContext_is_isolated_to_admin_assembly()
    {
        // Only Kartova.Organization.Infrastructure.Admin (and its consumers) may reference AdminOrganizationDbContext.
        var admin = typeof(Kartova.Organization.Infrastructure.Admin.AdminOrganizationDbContext);

        // Modules/Organization/Infrastructure (non-admin) must NOT reference the admin DbContext.
        var nonAdminInfraTypes = OrganizationInfrastructure.GetTypes()
            .Where(t => !t.IsGenericType);

        foreach (var t in nonAdminInfraTypes)
        {
            var refs = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(f => f.FieldType)
                .Concat(t.GetProperties().Select(p => p.PropertyType));
            refs.Should().NotContain(admin,
                because: $"{t.FullName} in the tenant-scoped Infrastructure assembly must not depend on AdminOrganizationDbContext");
        }
    }

    [Fact]
    public void Wolverine_middleware_project_exists()
    {
        // Sanity: ensure the Wolverine adapter skeleton is compiled and present.
        var mw = typeof(Kartova.SharedKernel.Wolverine.TenantScopeWolverineMiddleware);
        mw.GetMethod("BeforeAsync", BindingFlags.Static | BindingFlags.Public).Should().NotBeNull();
    }

    [Fact]
    public void Every_tenant_owned_entity_has_RLS_policy_in_a_migration()
    {
        // Find every class implementing ITenantOwned in any referenced assembly, then check
        // migration files in the corresponding Infrastructure assemblies contain an ENABLE
        // ROW LEVEL SECURITY for its table.
        var tenantOwnedTypes = new[] { typeof(Kartova.Organization.Domain.Organization) };

        foreach (var t in tenantOwnedTypes)
        {
            var tableName = t.Name.ToLowerInvariant() + "s"; // convention
            var migrationsDir = Path.Combine(
                Path.GetDirectoryName(t.Assembly.Location)!,
                "..", "..", "..", "..");
            var migrationSources = Directory.GetFiles(migrationsDir, "*InitialOrganization.cs", SearchOption.AllDirectories);
            migrationSources.Should().NotBeEmpty(because: $"expected a migration for {tableName}");

            var anyHasRls = migrationSources.Any(f =>
                File.ReadAllText(f).Contains("ENABLE ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase));
            anyHasRls.Should().BeTrue(
                because: $"migration for {tableName} must ENABLE ROW LEVEL SECURITY per ADR-0012/0090");
        }
    }

    [Fact]
    public void AddModuleDbContext_helper_is_exposed()
    {
        var type = typeof(Kartova.SharedKernel.Postgres.AddModuleDbContextExtensions);
        type.GetMethod("AddModuleDbContext", BindingFlags.Static | BindingFlags.Public).Should().NotBeNull();
        type.GetMethod("AddTenantScope", BindingFlags.Static | BindingFlags.Public).Should().NotBeNull();
    }

    [Fact]
    public void TestJwtSigner_is_not_referenced_outside_test_projects()
    {
        // Kartova.Api must NOT reference Kartova.Testing.Auth.
        var apiRefs = typeof(Program).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name);
        apiRefs.Should().NotContain("Kartova.Testing.Auth",
            because: "Production API must not reference test-only JWT signer");
    }
}
```

- [ ] **Step 2: Run**

Run: `cmd //c dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --nologo`
Expected: All tests pass (6 existing + 6 new).

- [ ] **Step 3: Commit**

```bash
git add tests/Kartova.ArchitectureTests/
git commit -m "test(arch): new rules for tenant-scope mechanism, bypass isolation, RLS-per-entity"
```

---

## Task 25: Kartova.Api.IntegrationTests — KeyCloak container auth smoke test

**Goal:** One dedicated test class + project that proves the full KeyCloak realm JSON + claim mapper + JwtBearer pipeline work end-to-end with a real KeyCloak testcontainer.

**Files:**
- Create: `tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj`
- Create: `tests/Kartova.Api.IntegrationTests/KeycloakContainerFixture.cs`
- Create: `tests/Kartova.Api.IntegrationTests/AuthSmokeTests.cs`

- [ ] **Step 1: Tests csproj**

File: `tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj`

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
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="7.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageReference Include="Testcontainers" Version="4.0.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.0.0" />
    <PackageReference Include="Testcontainers.Keycloak" Version="4.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Kartova.Api\Kartova.Api.csproj" />
    <ProjectReference Include="..\..\src\Modules\Organization\Kartova.Organization.Infrastructure\Kartova.Organization.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\Modules\Organization\Kartova.Organization.Infrastructure.Admin\Kartova.Organization.Infrastructure.Admin.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="..\..\deploy\keycloak\kartova-realm.json" Link="kartova-realm.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `KeycloakContainerFixture`**

File: `tests/Kartova.Api.IntegrationTests/KeycloakContainerFixture.cs`

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kartova.Api.IntegrationTests;

public sealed class KeycloakContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("kartova")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public KeycloakContainer Keycloak { get; } = new KeycloakBuilder()
        .WithImage("quay.io/keycloak/keycloak:26.1")
        .WithCommand("start-dev", "--import-realm")
        .WithResourceMapping(
            Path.Combine(AppContext.BaseDirectory, "kartova-realm.json"),
            "/opt/keycloak/data/import/kartova-realm.json")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/realms/kartova/.well-known/openid-configuration")))
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Keycloak.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(), Keycloak.DisposeAsync().AsTask());
    }

    public string KeycloakAuthority => $"{Keycloak.GetBaseAddress()}realms/kartova";
}
```

- [ ] **Step 3: `AuthSmokeTests`**

File: `tests/Kartova.Api.IntegrationTests/AuthSmokeTests.cs`

```csharp
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kartova.Api.IntegrationTests;

public class AuthSmokeTests : IClassFixture<KeycloakContainerFixture>, IAsyncLifetime
{
    private readonly KeycloakContainerFixture _fx;
    private WebApplicationFactory<Program>? _app;

    public AuthSmokeTests(KeycloakContainerFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        // Seed DB roles + one Org A row.
        await SeedPostgres();

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, c) =>
            {
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Kartova"] = AppConnectionString("kartova_app"),
                    ["ConnectionStrings:KartovaBypass"] = AppConnectionString("kartova_bypass_rls"),
                    ["Authentication:Authority"] = _fx.KeycloakAuthority,
                    ["Authentication:MetadataAddress"] = $"{_fx.KeycloakAuthority}/.well-known/openid-configuration",
                    ["Authentication:Audience"] = "kartova-api",
                    ["Authentication:RequireHttpsMetadata"] = "false",
                });
            });
        });

        await RunMigrationsAsync();
        await SeedOrgA();
    }

    public Task DisposeAsync()
    {
        _app?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Full_KeyCloak_realm_issues_token_and_API_accepts_it()
    {
        // ROPC grant
        using var oidc = new HttpClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "kartova-api",
            ["username"] = "admin@orga.kartova.local",
            ["password"] = "dev_pass",
            ["scope"] = "openid",
        });
        var tokenResp = await oidc.PostAsync($"{_fx.KeycloakAuthority}/protocol/openid-connect/token", form);
        tokenResp.EnsureSuccessStatusCode();
        var payload = await tokenResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var accessToken = payload!["access_token"].ToString()!;

        // Call API
        var client = _app!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.GetAsync("/api/v1/organizations/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private string AppConnectionString(string user) => new NpgsqlConnectionStringBuilder(_fx.Postgres.GetConnectionString())
    {
        Username = user,
        Password = user == "kartova_bypass_rls" ? "dev_only" : "dev",
    }.ToString();

    private async Task SeedPostgres()
    {
        await using var conn = new NpgsqlConnection(_fx.Postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE ROLE migrator WITH LOGIN PASSWORD 'dev' CREATEDB;
            CREATE ROLE kartova_app WITH LOGIN PASSWORD 'dev';
            CREATE ROLE kartova_bypass_rls WITH LOGIN PASSWORD 'dev_only' BYPASSRLS;
            GRANT CONNECT ON DATABASE kartova TO kartova_app, kartova_bypass_rls;
            ALTER SCHEMA public OWNER TO migrator;
            GRANT USAGE, CREATE ON SCHEMA public TO kartova_app, kartova_bypass_rls;
            GRANT CREATE ON DATABASE kartova TO kartova_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kartova_app, kartova_bypass_rls;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RunMigrationsAsync()
    {
        using var scope = _app!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Kartova.Organization.Infrastructure.OrganizationDbContext>();
        await db.Database.MigrateAsync();
    }

    private async Task SeedOrgA()
    {
        await using var conn = new NpgsqlConnection(AppConnectionString("kartova_bypass_rls"));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO organizations (id, tenant_id, name, created_at) VALUES ($1, $2, 'Org A', now())";
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        await cmd.ExecuteNonQueryAsync();
    }
}
```

- [ ] **Step 4: Add to slnx + run**

Run:
```bash
cmd //c dotnet sln Kartova.slnx add tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj
cmd //c dotnet test tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj --nologo
```
Expected: 1 test passes in ~30s.

- [ ] **Step 5: Commit**

```bash
git add tests/Kartova.Api.IntegrationTests/ Kartova.slnx
git commit -m "test(api): KeyCloak container auth smoke test"
```

---

## Task 26: CI workflow update

**Goal:** Add the two new test projects to `.github/workflows/ci.yml` so they run on push/PR.

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Inspect current CI**

Run: `type .github\workflows\ci.yml`
Expected: shows backend / frontend / helm jobs.

- [ ] **Step 2: Ensure backend test step runs the full solution**

In the backend job, the test step should already be `dotnet test Kartova.slnx --no-build --configuration Release`. If it specifies individual projects, update to use `Kartova.slnx`. This automatically picks up the new tests.

If the file already runs `dotnet test Kartova.slnx`, no change is needed — just confirm with:

Run: `cmd //c findstr /c:"dotnet test" .github\workflows\ci.yml`
Expected: line includes `Kartova.slnx`.

- [ ] **Step 3: Ensure Docker is available for Testcontainers**

Confirm the backend CI job's runner has Docker. GitHub's `ubuntu-latest` runners include Docker by default; the existing Slice 1 Catalog integration tests already rely on it. No change needed.

- [ ] **Step 4: Commit if CI file changed**

If any edit was needed:
```bash
git add .github/workflows/ci.yml
git commit -m "ci: ensure Slice 2 test projects run on push/PR"
```

If no change was needed, skip.

---

## Task 27: Update CHECKLIST.md for Slice 2 stories

**Goal:** Mark E-01.F-03.S-01 (multi-tenant schema), E-01.F-04.S-01 (KeyCloak), E-01.F-04.S-02 (JWT middleware), E-01.F-08.S-03 (RLS isolation) as complete.

**Files:**
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Update checkbox statuses**

Open `docs/product/CHECKLIST.md`. For each of these story ids, change the leading `- [ ]` to `- [x]`:
- `E-01.F-03.S-01`
- `E-01.F-04.S-01`
- `E-01.F-04.S-02`
- `E-01.F-08.S-03`

If the checklist uses a different format (e.g., a table with a "Done" column), set the appropriate column value to ✅ / Yes / x.

- [ ] **Step 2: Commit**

```bash
git add docs/product/CHECKLIST.md
git commit -m "docs(checklist): mark Slice 2 stories complete (E-01.F-03.S-01, F-04.S-01/02, F-08.S-03)"
```

---

## Task 28: Verification + PR

**Goal:** Final end-to-end verification + push/merge.

- [ ] **Step 1: Full build**

Run: `cmd //c dotnet build Kartova.slnx --configuration Release --nologo`
Expected: Build succeeded. 0 Errors.

- [ ] **Step 2: Full test**

Run: `cmd //c dotnet test Kartova.slnx --configuration Release --no-build --nologo`
Expected: all tests pass across all projects (architecture + unit + integration + auth smoke).

- [ ] **Step 3: Docker compose end-to-end**

Run:
```bash
cmd //c docker compose down -v
cmd //c docker compose up --build -d
```
Wait until `api` is healthy. Then:

```bash
# ROPC grant against KeyCloak
cmd //c curl -s -X POST http://localhost:8180/realms/kartova/protocol/openid-connect/token ^
  -d grant_type=password -d client_id=kartova-api ^
  -d username=admin@orga.kartova.local -d password=dev_pass
```

Expected: JSON with `access_token`. Copy the `access_token` value.

```bash
cmd //c curl -s http://localhost:8080/api/v1/organizations/me -H "Authorization: Bearer <TOKEN>"
```

Expected: 404 with problem-details (no Org A row seeded yet — this is correct; the `POST /api/v1/admin/organizations` endpoint or a dev seed would populate it). Alternatively, create one via the admin path with a platform-admin token:

```bash
# Get platform-admin token
cmd //c curl -s -X POST http://localhost:8180/realms/kartova/protocol/openid-connect/token ^
  -d grant_type=password -d client_id=kartova-api ^
  -d username=platform-admin@kartova.local -d password=dev_pass

cmd //c curl -s -X POST http://localhost:8080/api/v1/admin/organizations ^
  -H "Authorization: Bearer <ADMIN_TOKEN>" ^
  -H "Content-Type: application/json" ^
  -d "{\"name\":\"Org A manual\"}"
```

Expected: 201 Created with OrganizationDto. Then `GET /organizations/me` with an Org A user token returns 200 if their `tenant_id` claim matches a seeded row.

- [ ] **Step 4: Create PR**

```bash
git checkout -b slice-2/auth-multitenancy
git push -u origin slice-2/auth-multitenancy
cmd //c gh pr create --title "Slice 2 — Auth + Multi-Tenancy" --body "See docs/superpowers/specs/2026-04-22-slice-2-auth-multitenancy-design.md"
```

- [ ] **Step 5: Merge after review**

After CI passes and review is complete:
```bash
git checkout master
git merge --ff-only slice-2/auth-multitenancy
git push origin master
```
