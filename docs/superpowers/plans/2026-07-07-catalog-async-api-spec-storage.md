# Async API (unified entity) + stored spec artifacts — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make API a single unified catalog entity keyed by `ApiStyle` (async = new `AsyncApi` value) and let any API store its spec document (OpenAPI/AsyncAPI) as `text` in Postgres via a `PUT`/`GET /apis/{id}/spec` sub-resource.

**Architecture:** Add `AsyncApi` to the existing `ApiStyle` enum (fully additive — no switches; `Enum.IsDefined`/`TryParse` absorb it). Store spec documents in a new RLS-scoped `catalog_api_specs` table (1:1 with `catalog_apis`, `ON DELETE CASCADE`), written by a dedicated upsert endpoint that reuses the shipped team-membership gate and `catalog.apis.register` permission. `ApiResponse` gains a computed `HasSpec` init-property (additive, non-breaking). Backend-only; UI deferred.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs · EF Core (Npgsql) · Wolverine (not used in read/write path here — direct-dispatch handlers, ADR-0093) · PostgreSQL 18 + RLS · MSTest v4 + NSubstitute · Testcontainers · React/TS (forced label touch only).

## Global Constraints

- **Solution file:** `Kartova.slnx` (ADR-0082). Modular monolith — Catalog changes stay inside `src/Modules/Catalog/**`; cross-module only via `IMessageBus`/Kafka (none needed here).
- **Windows shell:** `cmd //c "dotnet ..."` (double-slash MSYS workaround) or PowerShell for `dotnet`/`dotnet ef`. Git Bash lacks `grep -P` (use `-E`/`Select-String`).
- **Warnings-as-errors:** full solution builds with `TreatWarningsAsErrors=true`, 0 warnings (gate 1).
- **Coverage exclusion:** every new `*Request`/`*Response`/DTO carries `[ExcludeFromCodeCoverage]` (enforced by `ContractsCoverageRules`).
- **Tenant scope / DB (ADR-0090):** all tenant-scoped DB work runs inside `ITenantScope`; register module DbContexts via `AddModuleDbContext<T>`. Handlers never touch the scope — transport (endpoint filter) does `Begin`/`Commit`. `TenantId`/`CreatedByUserId`/`CreatedAt` come from `ITenantContext`/`ICurrentUser`/`TimeProvider`, never payload.
- **RLS on every tenant-owned table:** `ENABLE` + `FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy `USING (tenant_id = current_setting('app.current_tenant_id')::uuid)`, hand-added to the generated migration `Up()` (mirror `20260703161759_AddApis.cs`).
- **Migrations never run at startup (ADR-0085):** `Kartova.Migrator` container / EF migration. Add via `--startup-project src/Kartova.Migrator --context CatalogDbContext`.
- **List endpoints (ADR-0095):** N/A this slice — no new list endpoint (`/apis/{id}/spec` is a single-resource sub-route).
- **Enum wire form:** `JsonStringEnumConverter` + `JsonNamingPolicy.CamelCase` → `AsyncApi` serializes as `"asyncApi"` (cf. `GraphQL`→`"graphQL"`). ADR-0109.
- **No new `KartovaPermission`** — spec-write reuses `catalog.apis.register`; the permission 5-sync is **not** triggered.
- **DoD:** eight always-blocking gates + gate-6 (mutation) **blocking** here (Domain+Application logic changed). Ledger at `docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage/dod.md`.

---

## Impact Analysis (codelens/LSP)

**Method:** Built-in `LSP` (roslyn-language-server) was **unavailable this session** (`Command 'roslyn-language-server' not found`); codebase-memory MCP does **not** index this repo. Per the impact-analysis degradation clause, the table below is grounded with **`Grep` over `src/`** as a stopgop. **Re-run `find_references`/`find_callers` (roslyn-codelens or LSP) at execution time before editing `ApiStyle`/`ApiResponse`; add a task for any caller not in this table.**

| Changed symbol | Change | Tool run | Callers / refs | Notable call sites | Covered by task |
|----------------|--------|----------|----------------|--------------------|-----------------|
| `Kartova.Catalog.Domain.ApiStyle` | behavior (append enum value `AsyncApi`) | `grep ApiStyle src/` — **LSP unavailable** | 40 refs, all in `src/Modules/Catalog/**` (0 other modules) | `Api.Create` (`Enum.IsDefined`), `CatalogEndpointDelegates.cs:606` (`Enum.TryParse` style filter), `ApiSortSpecs` (orders by enum value), test literals | Task 2 |
| `Kartova.Catalog.Contracts.ApiResponse` | signature (add `HasSpec` **init prop**, not positional) | `grep ApiResponse` | Construction: 1 site (`ApiResponseExtensions.ToResponse`); consumers: `Get/ListApisHandler`, `RegisterApiHandler`, tests | Additive init prop ⇒ `ToResponse` unaffected; handlers opt-in via `with { HasSpec = … }` | Tasks 5, 6 |
| `ApiResponseExtensions.ToResponse` | unchanged | `grep ToResponse` | 3 call sites (Register/Get/List handlers) | signature unchanged — no caller edit | — |
| (TS) `API_STYLE_LABEL: Record<ApiStyleValue, string>` | forced (total Record over regenerated union) | `grep web/src` | `registerApi.ts:13`; consumed by RegisterApiDialog/ApisListPage | Once OpenAPI snapshot regenerates with `"asyncApi"`, `tsc` **fails** until label added | Task 8 |

**Blast-radius notes:** No exhaustive `switch`/pattern-match on `ApiStyle` exists in any module (grep-confirmed across `src/`), so appending `AsyncApi` is additive at every C# site. **Append at the enum's end** — the only order-sensitive site is `ListApisPaginationTests` byStyle-desc, which seeds only Rest/Grpc/GraphQL and stays green. The one *forced* edit outside the backend is the TS total-Record label (Task 8) — same drift-trap class as the permission 5-sync (Backend CI green, Frontend CI red if missed). `ApiSpec`/`ApiMediaType`/`Upsert`/`Get` handlers + endpoints are all **new code** (no existing-caller blast radius).

**Coverage check:** every reference above is handled — `ApiStyle` (Task 2), `ApiResponse.HasSpec` (Tasks 5–6), TS label (Task 8). No uncovered callers found in the grep stopgap; re-confirm with codelens/LSP at execution time.

---

## File Structure

**Created**
- `src/Modules/Catalog/Kartova.Catalog.Domain/ApiSpec.cs` — spec artifact entity (validation lives here).
- `src/Modules/Catalog/Kartova.Catalog.Domain/ApiMediaType.cs` — media-type allowlist.
- `src/Modules/Catalog/Kartova.Catalog.Application/UpsertApiSpecCommand.cs`
- `src/Modules/Catalog/Kartova.Catalog.Application/GetApiSpecQuery.cs`
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApiSpecConfiguration.cs`
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/UpsertApiSpecHandler.cs`
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiSpecHandler.cs`
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<ts>_AddApiSpec.cs` (generated + hand-edited RLS)
- `src/Modules/Catalog/Kartova.Catalog.Tests/ApiSpecTests.cs`
- `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ApiSpecTests.cs`
- `docs/architecture/decisions/ADR-0112-api-spec-artifacts-stored-in-postgres.md`
- `docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage/{dod.md,gate-findings.yaml}`

**Modified**
- `Kartova.Catalog.Domain/ApiStyle.cs` — `+ AsyncApi`, comment fix.
- `Kartova.Catalog.Contracts/ApiResponse.cs` — `+ bool HasSpec` init prop.
- `Kartova.Catalog.Application/CatalogAuditActions.cs` — `+ ApiSpecUpdated`.
- `Kartova.Catalog.Infrastructure/CatalogDbContext.cs` — `+ DbSet<ApiSpec>` + apply config.
- `Kartova.Catalog.Infrastructure/CatalogModule.cs` — register 2 handlers + map 2 routes.
- `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` — `UpsertApiSpecAsync`, `GetApiSpecAsync`, `LoadAndAuthorizeApiAsync`.
- `Kartova.Catalog.Infrastructure/GetApiByIdHandler.cs` + `ListApisHandler.cs` — populate `HasSpec`.
- `Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs` — +2 rows.
- `docs/architecture/decisions/ADR-0111-*.md` + `docs/architecture/decisions/README.md` — amendment + index.
- `CLAUDE.md` — refresh the ADR-0111 guardrail line.
- `web/src/features/catalog/schemas/registerApi.ts` — `+ asyncApi` in `API_STYLES` + `API_STYLE_LABEL`; regenerate snapshot/client.
- `docs/product/CHECKLIST.md` — mark S-02.

---

## Task 1: ADRs (ADR-0112 new + ADR-0111 amendment)

**Files:**
- Create: `docs/architecture/decisions/ADR-0112-api-spec-artifacts-stored-in-postgres.md`
- Modify: `docs/architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md` (add amendment note)
- Modify: `docs/architecture/decisions/README.md` (index row + keywords)
- Modify: `CLAUDE.md` (ADR-0111 guardrail line)

**Interfaces:** Produces the governing decisions the rest of the slice cites. No code.

- [ ] **Step 1: Write ADR-0112** using the Nygard template (mirror an existing ADR's front-matter/section shape). Content:
  - **Status:** Accepted (2026-07-07).
  - **Context:** API family must store OpenAPI/AsyncAPI (future WSDL) documents. Sizes 20–50 KB typical, ~1 MB tail. Options: Postgres `text` vs MinIO/S3 (ADR-0004).
  - **Decision:** Store spec documents as `text` in a dedicated RLS-scoped `catalog_api_specs` table, transactional with the owning API. `media_type` column tags serialization; semantic format derives from `Api.Style`. Not MinIO.
  - **Consequences:** free tenant isolation (RLS) + transactional integrity + uniform ops; TOAST handles the 1 MB tail. **Revisit → MinIO** if E-21 version-history makes this many-versions × ~1 MB × many-APIs and table bloat bites. Narrows ADR-0004 for this data class; distinct from ADR-0034 (OpenAPI *auto-render*).

- [ ] **Step 2: Amend ADR-0111** — add a dated amendment block:

```markdown
### Amendment 2026-07-07 (E-02.F-03.S-02) — unified API entity; async is a Style value

Supersedes the original §1/§Consequences framing that async "adds messaging protocol + channels" as structured columns. API is ONE unified entity keyed by `Style ∈ {Rest, Grpc, GraphQL, AsyncApi}`. Async's protocol/channels/operations are carried by the stored AsyncAPI document (see ADR-0112), NOT structured columns. Structured `publishes-to`/`subscribes-from` edges + Broker linkage remain deferred (FU-C, needs E-02.F-04) and, when built, parse channels from the stored doc or a dedicated edge-authoring path.
```

- [ ] **Step 3: Update `README.md`** ADR index — add the ADR-0112 row and keyword entries (`spec storage`, `api_spec`, `AsyncAPI`), and note ADR-0111 "amended 2026-07-07".

- [ ] **Step 4: Update `CLAUDE.md`** — replace the ADR-0111 guardrail bullet's tail with the unified-entity wording:

```
- **API entity model:** API is a first-class catalog entity (`EntityKind.Api`), one unified aggregate keyed by `Style` (`Rest`/`Grpc`/`GraphQL`/`AsyncApi`); async detail lives in the stored spec doc, not columns. Spec documents stored as `text` in `catalog_api_specs` (RLS, 1:1, ADR-0112). Provider/instance/consumer links are all relationship edges … (ADR-0111 **amended 2026-07-07 unified-entity + spec-storage**)
```

- [ ] **Step 5: Commit**

```bash
git add docs/architecture/decisions/ADR-0112-api-spec-artifacts-stored-in-postgres.md \
        docs/architecture/decisions/ADR-0111-*.md docs/architecture/decisions/README.md CLAUDE.md
git commit -m "docs(adr): ADR-0112 spec-artifact storage + ADR-0111 unified-API-entity amendment"
```

---

## Task 2: `ApiStyle.AsyncApi`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/ApiStyle.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ApiTests.cs`

**Interfaces:** Produces `ApiStyle.AsyncApi` (the 4th member, value 3). Consumed by Tasks 5–8 and the wire contract.

- [ ] **Step 1: Write the failing test** — append to `ApiTests.cs`:

```csharp
[TestMethod]
public void Create_accepts_AsyncApi_style()
{
    var a = Api.Create("orders-events", "Order events.", ApiStyle.AsyncApi, "1.0", null,
        Creator, Team, Tenant, TimeProvider.System);
    Assert.AreEqual(ApiStyle.AsyncApi, a.Style);
}
```

- [ ] **Step 2: Run — expect FAIL** (compile error: `AsyncApi` undefined)

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter Create_accepts_AsyncApi_style"
```
Expected: build error `'ApiStyle' does not contain a definition for 'AsyncApi'`.

- [ ] **Step 3: Add the enum value + fix the comment** — `ApiStyle.cs`:

```csharp
namespace Kartova.Catalog.Domain;

/// <summary>API style (ADR-0111, amended 2026-07-07). One unified API entity keyed by this
/// value; async ("AsyncApi") is a style, not a separate entity — its protocol/channels live in
/// the stored AsyncAPI document (ADR-0112), not in columns. WSDL is a planned future member.</summary>
public enum ApiStyle
{
    Rest,
    Grpc,
    GraphQL,
    AsyncApi,   // append at end — keeps existing smallint values + byStyle sort stable
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter Create_accepts_AsyncApi_style"
```
Expected: PASS. (`Create(style:(ApiStyle)999)` invalid-style test still throws — 999 remains undefined.)

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/ApiStyle.cs src/Modules/Catalog/Kartova.Catalog.Tests/ApiTests.cs
git commit -m "feat(catalog): add ApiStyle.AsyncApi (unified API entity, ADR-0111 amended)"
```

---

## Task 3: `ApiSpec` domain type + `ApiMediaType`

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/ApiMediaType.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/ApiSpec.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ApiSpecTests.cs`

**Interfaces:**
- Produces `ApiMediaType.IsAllowed(string) : bool` and constants `ApplicationJson`, `ApplicationYaml`.
- Produces `ApiSpec` with `static ApiSpec Create(ApiId apiId, TenantId tenantId, string content, string mediaType, Guid createdByUserId, DateTimeOffset createdAt)` and `void Replace(string content, string mediaType, Guid updatedByUserId, DateTimeOffset updatedAt)`. Props: `ApiId ApiId`, `TenantId TenantId`, `string Content`, `string MediaType`, `Guid CreatedByUserId`, `DateTimeOffset CreatedAt`, `uint Xmin`. Const `MaxContentBytes = 5 * 1024 * 1024`.

- [ ] **Step 1: Write failing tests** — `ApiSpecTests.cs`:

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ApiSpecTests
{
    private static readonly ApiId Api = ApiId.New();
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [TestMethod]
    public void Create_valid_json_spec()
    {
        var s = ApiSpec.Create(Api, Tenant, "{}", ApiMediaType.ApplicationJson, User, Now);
        Assert.AreEqual("{}", s.Content);
        Assert.AreEqual(ApiMediaType.ApplicationJson, s.MediaType);
        Assert.AreEqual(Api.Value, s.ApiId.Value);
    }

    [TestMethod]
    public void Create_rejects_empty_content()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ApiSpec.Create(Api, Tenant, "   ", ApiMediaType.ApplicationJson, User, Now));

    [TestMethod]
    public void Create_rejects_oversized_content()
    {
        var big = new string('x', ApiSpec.MaxContentBytes + 1);
        Assert.ThrowsExactly<ArgumentException>(
            () => ApiSpec.Create(Api, Tenant, big, ApiMediaType.ApplicationJson, User, Now));
    }

    [TestMethod]
    public void Create_rejects_unknown_media_type()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ApiSpec.Create(Api, Tenant, "{}", "text/xml", User, Now));

    [TestMethod]
    public void Replace_updates_content_and_media_type()
    {
        var s = ApiSpec.Create(Api, Tenant, "{}", ApiMediaType.ApplicationJson, User, Now);
        s.Replace("channels: {}", ApiMediaType.ApplicationYaml, User, Now.AddMinutes(1));
        Assert.AreEqual("channels: {}", s.Content);
        Assert.AreEqual(ApiMediaType.ApplicationYaml, s.MediaType);
    }

    [TestMethod]
    public void IsAllowed_matches_only_json_and_yaml()
    {
        Assert.IsTrue(ApiMediaType.IsAllowed("application/json"));
        Assert.IsTrue(ApiMediaType.IsAllowed("application/yaml"));
        Assert.IsFalse(ApiMediaType.IsAllowed("text/xml"));
        Assert.IsFalse(ApiMediaType.IsAllowed(""));
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (types undefined)

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter ApiSpecTests"
```

- [ ] **Step 3: Implement `ApiMediaType.cs`**

```csharp
namespace Kartova.Catalog.Domain;

/// <summary>Allowed serializations for a stored API spec document (ADR-0112). The semantic
/// format (OpenAPI vs AsyncAPI) derives from <see cref="Api.Style"/>; this only constrains the
/// wire serialization we accept and echo back. XML/WSDL is a planned future member.</summary>
public static class ApiMediaType
{
    public const string ApplicationJson = "application/json";
    public const string ApplicationYaml = "application/yaml";

    public static bool IsAllowed(string? mediaType)
        => mediaType is ApplicationJson or ApplicationYaml;
}
```

- [ ] **Step 4: Implement `ApiSpec.cs`**

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

/// <summary>The current stored spec document (OpenAPI/AsyncAPI) for one <see cref="Api"/>
/// (ADR-0112). 1:1 with the owning API (unique <c>api_id</c>); versions deferred to E-21.
/// Content is opaque text — not parsed or validated for schema correctness this slice.</summary>
public sealed class ApiSpec : ITenantOwned
{
    public const int MaxContentBytes = 5 * 1024 * 1024;   // 5 MiB hard cap

    private Guid _id;

    public Guid Id => _id;
    public ApiId ApiId { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string MediaType { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Xmin { get; private set; }

    private ApiSpec() { }   // EF

    public static ApiSpec Create(
        ApiId apiId, TenantId tenantId, string content, string mediaType,
        Guid createdByUserId, DateTimeOffset createdAt)
    {
        Validate(content, mediaType);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));
        return new ApiSpec
        {
            _id = Guid.NewGuid(),
            ApiId = apiId,
            TenantId = tenantId,
            Content = content,
            MediaType = mediaType,
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt,
        };
    }

    /// <summary>Replaces the stored document in place (upsert path). Provenance stays the
    /// original <see cref="CreatedByUserId"/>/<see cref="CreatedAt"/> — one current spec, no history.</summary>
    public void Replace(string content, string mediaType, Guid updatedByUserId, DateTimeOffset updatedAt)
    {
        Validate(content, mediaType);
        if (updatedByUserId == Guid.Empty)
            throw new ArgumentException("updatedByUserId is required.", nameof(updatedByUserId));
        Content = content;
        MediaType = mediaType;
    }

    private static void Validate(string content, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("API spec content must not be empty.", nameof(content));
        if (System.Text.Encoding.UTF8.GetByteCount(content) > MaxContentBytes)
            throw new ArgumentException($"API spec content must be <= {MaxContentBytes} bytes.", nameof(content));
        if (!ApiMediaType.IsAllowed(mediaType))
            throw new ArgumentException("API spec media type must be application/json or application/yaml.", nameof(mediaType));
    }
}
```

- [ ] **Step 5: Run — expect PASS**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter ApiSpecTests"
```

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/ApiSpec.cs \
        src/Modules/Catalog/Kartova.Catalog.Domain/ApiMediaType.cs \
        src/Modules/Catalog/Kartova.Catalog.Tests/ApiSpecTests.cs
git commit -m "feat(catalog): ApiSpec domain type + media-type allowlist (ADR-0112)"
```

---

## Task 4: EF config + `catalog_api_specs` migration

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApiSpecConfiguration.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<ts>_AddApiSpec.cs`

**Interfaces:** Produces `CatalogDbContext.ApiSpecs` (`DbSet<ApiSpec>`), table `catalog_api_specs` with unique `api_id`, FK → `catalog_apis(id)` `ON DELETE CASCADE`, RLS.

- [ ] **Step 1: Implement `EfApiSpecConfiguration.cs`**

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfApiSpecConfiguration : IEntityTypeConfiguration<ApiSpec>
{
    public void Configure(EntityTypeBuilder<ApiSpec> b)
    {
        b.ToTable("catalog_api_specs");

        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.HasKey(x => x.Id);

        b.Property(x => x.ApiId)
            .HasConversion(v => v.Value, v => new ApiId(v))
            .HasColumnName("api_id")
            .IsRequired();
        b.HasIndex(x => x.ApiId).IsUnique().HasDatabaseName("ux_catalog_api_specs_api_id");

        b.Property(x => x.TenantId)
            .HasConversion(v => v.Value, v => new TenantId(v))
            .HasColumnName("tenant_id")
            .IsRequired();
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_api_specs_tenant_id");

        b.Property(x => x.Content).HasColumnName("content").HasColumnType("text").IsRequired();
        b.Property(x => x.MediaType).HasColumnName("media_type").HasMaxLength(64).IsRequired();
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property(x => x.Xmin)
            .HasColumnName("xmin").HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate().IsRowVersion().IsConcurrencyToken();
    }
}
```

- [ ] **Step 2: Wire into `CatalogDbContext.cs`** — add the `DbSet` next to `Apis` and apply the config where sibling configs are applied (`ApplyConfiguration(new EfApiSpecConfiguration())` or via assembly scan if that's the existing pattern — match how `EfApiConfiguration` is registered):

```csharp
public DbSet<ApiSpec> ApiSpecs => Set<ApiSpec>();
// in OnModelCreating (only if configs are applied one-by-one, mirroring EfApiConfiguration):
modelBuilder.ApplyConfiguration(new EfApiSpecConfiguration());
```

- [ ] **Step 3: Generate the migration**

```bash
cmd //c "dotnet ef migrations add AddApiSpec --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Migrator --context CatalogDbContext"
```
Expected: new `Migrations/<ts>_AddApiSpec.cs` + `.Designer.cs` + updated `CatalogDbContextModelSnapshot.cs`.

- [ ] **Step 4: Hand-add RLS + FK to the generated `Up()`/`Down()`** (mirror `20260703161759_AddApis.cs`). Ensure `Up()` contains the FK with cascade delete and the RLS block; `Down()` drops policy then table:

```csharp
// in Up(), the CreateTable constraints block:
constraints: table =>
{
    table.PrimaryKey("PK_catalog_api_specs", x => x.id);
    table.ForeignKey(
        name: "fk_catalog_api_specs_apis_api_id",
        column: x => x.api_id,
        principalTable: "catalog_apis",
        principalColumn: "id",
        onDelete: ReferentialAction.Cascade);
});
// after indexes:
migrationBuilder.Sql(@"
ALTER TABLE catalog_api_specs ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_api_specs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_api_specs
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
// Down():
migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON catalog_api_specs;
ALTER TABLE catalog_api_specs DISABLE ROW LEVEL SECURITY;
");
migrationBuilder.DropTable(name: "catalog_api_specs");
```

- [ ] **Step 5: Build to verify the model + migration compile**

```bash
cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure"
```
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApiSpecConfiguration.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/
git commit -m "feat(catalog): catalog_api_specs table + EF config + RLS migration (ADR-0112)"
```

---

## Task 5: Application layer (command/query, audit action, `HasSpec` contract)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/UpsertApiSpecCommand.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/GetApiSpecQuery.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApiResponse.cs`

**Interfaces:**
- Produces `UpsertApiSpecCommand(Guid ApiId, string Content, string MediaType)`, `GetApiSpecQuery(Guid ApiId)`.
- Produces `CatalogAuditActions.ApiSpecUpdated = "api.spec.updated"`.
- Produces `ApiResponse.HasSpec { get; init; }` (defaults `false`).

- [ ] **Step 1: Create `UpsertApiSpecCommand.cs`**

```csharp
namespace Kartova.Catalog.Application;

/// <summary>Upsert (create-or-replace) the stored spec document for an API. Content/media-type
/// are validated by <c>ApiSpec</c>; the caller/clock/tenant come from context, not the command.</summary>
public sealed record UpsertApiSpecCommand(Guid ApiId, string Content, string MediaType);
```

- [ ] **Step 2: Create `GetApiSpecQuery.cs`**

```csharp
namespace Kartova.Catalog.Application;

public sealed record GetApiSpecQuery(Guid ApiId);
```

- [ ] **Step 3: Add the audit action** — in `CatalogAuditActions.cs`, after `ApiRegistered`:

```csharp
    public const string ApiSpecUpdated = "api.spec.updated";
```

- [ ] **Step 4: Add `HasSpec` to `ApiResponse.cs`** (init prop, mirrors `CreatedBy` — additive, non-breaking):

```csharp
    public UserDisplayInfo? CreatedBy { get; init; }

    /// <summary>True when a spec document is stored for this API (ADR-0112). Computed by the read
    /// handlers via EXISTS — the document itself is never carried on this response.</summary>
    public bool HasSpec { get; init; }
```

- [ ] **Step 5: Build**

```bash
cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Application src/Modules/Catalog/Kartova.Catalog.Contracts"
```
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/UpsertApiSpecCommand.cs \
        src/Modules/Catalog/Kartova.Catalog.Application/GetApiSpecQuery.cs \
        src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs \
        src/Modules/Catalog/Kartova.Catalog.Contracts/ApiResponse.cs
git commit -m "feat(catalog): UpsertApiSpec/GetApiSpec contracts + api.spec.updated + HasSpec flag"
```

---

## Task 6: Handlers (upsert, get, HasSpec enrichment)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/UpsertApiSpecHandler.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiSpecHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiByIdHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApisHandler.cs`

**Interfaces:**
- `UpsertApiSpecHandler.Handle(UpsertApiSpecCommand, CatalogDbContext, ITenantContext, ICurrentUser, IAuditWriter, CancellationToken) : Task<bool>` — returns `true` when created, `false` when replaced (drives 201 vs 204). Assumes the API's existence + authz were already checked by the delegate.
- `GetApiSpecHandler.Handle(GetApiSpecQuery, CatalogDbContext, CancellationToken) : Task<(string Content, string MediaType)?>` — null when absent (RLS-scoped).
- `GetApiByIdHandler`/`ListApisHandler` now set `HasSpec`.

- [ ] **Step 1: Implement `UpsertApiSpecHandler.cs`**

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Direct-dispatch handler for <see cref="UpsertApiSpecCommand"/> (ADR-0093).
/// Create-or-replace the 1:1 spec row. Tenant/clock/caller from context (ADR-0090). Audit
/// written in-transaction (fail-closed). The delegate has already loaded the API and run the
/// team-membership gate, so this trusts the api id exists in scope.</summary>
public sealed class UpsertApiSpecHandler(TimeProvider clock)
{
    public async Task<bool> Handle(
        UpsertApiSpecCommand cmd, CatalogDbContext db, ITenantContext tenant,
        ICurrentUser user, IAuditWriter audit, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var existing = await db.ApiSpecs.FirstOrDefaultAsync(s => s.ApiId == new ApiId(cmd.ApiId), ct);
        bool created;
        if (existing is null)
        {
            db.ApiSpecs.Add(ApiSpec.Create(
                new ApiId(cmd.ApiId), tenant.Id, cmd.Content, cmd.MediaType, user.UserId, now));
            created = true;
        }
        else
        {
            existing.Replace(cmd.Content, cmd.MediaType, user.UserId, now);
            created = false;
        }

        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ApiSpecUpdated,
            CatalogAuditTargetTypes.Api,
            cmd.ApiId.ToString(),
            new Dictionary<string, string?>
            {
                ["mediaType"] = cmd.MediaType,
                ["created"] = created ? "true" : "false",
            }), ct);

        return created;
    }
}
```

- [ ] **Step 2: Implement `GetApiSpecHandler.cs`**

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="GetApiSpecQuery"/>. RLS scopes visibility; returns null when
/// no spec is stored (or the API is invisible cross-tenant).</summary>
public sealed class GetApiSpecHandler
{
    public async Task<(string Content, string MediaType)?> Handle(
        GetApiSpecQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var row = await db.ApiSpecs
            .Where(s => s.ApiId == new ApiId(q.ApiId))
            .Select(s => new { s.Content, s.MediaType })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.Content, row.MediaType);
    }
}
```

- [ ] **Step 3: Enrich `HasSpec` in `GetApiByIdHandler.cs`** — replace the body:

```csharp
    public async Task<ApiResponse?> Handle(GetApiByIdQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var api = await db.Apis.FirstOrDefaultAsync(ApiSortSpecs.IdEquals(q.Id), ct);
        if (api is null) return null;

        var hasSpec = await db.ApiSpecs.AnyAsync(s => s.ApiId == api.Id, ct);
        var creator = await directory.GetAsync(api.CreatedByUserId, ct);
        return api.ToResponse() with { CreatedBy = creator, HasSpec = hasSpec };
    }
```

- [ ] **Step 4: Enrich `HasSpec` in `ListApisHandler.cs`** — after `creators` are fetched, compute a `HashSet<Guid>` of API ids that have specs in one query, then set `HasSpec` per item. Replace the items projection:

```csharp
        var pageApiIds = page.Items.Select(a => a.Id.Value).ToList();
        var idsWithSpec = new HashSet<Guid>(await db.ApiSpecs
            .Where(s => pageApiIds.Contains(s.ApiId.Value))
            .Select(s => s.ApiId.Value)
            .ToListAsync(ct));

        var items = page.Items
            .Select(a =>
            {
                var resp = a.ToResponse() with { HasSpec = idsWithSpec.Contains(a.Id.Value) };
                return creators.TryGetValue(a.CreatedByUserId, out var creator)
                    ? resp with { CreatedBy = creator }
                    : resp;
            })
            .ToList();
```

- [ ] **Step 5: Build**

```bash
cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure"
```
Expected: 0 warnings, 0 errors. (Integration behavior is verified in Task 7.)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/UpsertApiSpecHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiSpecHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiByIdHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApisHandler.cs
git commit -m "feat(catalog): ApiSpec upsert/get handlers + HasSpec enrichment"
```

---

## Task 7: Endpoints + routes + module registration + real-seam tests

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ApiSpecTests.cs`

**Interfaces:**
- `PUT /api/v1/catalog/apis/{id:guid}/spec` (raw body + `Content-Type`; `catalog.apis.register` + membership) → 201 (create) / 204 (replace); 400 empty/oversized; 415 bad media type; 403 non-member; 404 unknown API.
- `GET /api/v1/catalog/apis/{id:guid}/spec` (`catalog.read`) → 200 raw text + stored `Content-Type`; 404 absent.

- [ ] **Step 1: Write failing real-seam tests** — `Kartova.Catalog.IntegrationTests/ApiSpecTests.cs`. Mirror `RegisterApiTests` fixture usage (`KartovaApiFixtureBase`, real Postgres/RLS + real JWT). Register an API first (POST `/apis`), then exercise the spec sub-resource:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class ApiSpecTests : KartovaApiFixtureBase   // match RegisterApiTests' base/attrs
{
    private static StringContent Yaml(string s) => new(s, Encoding.UTF8, ApiMediaType.ApplicationYaml);
    private static StringContent Json(string s) => new(s, Encoding.UTF8, ApiMediaType.ApplicationJson);

    private async Task<Guid> RegisterApiAsync(HttpClient client, Guid teamId, ApiStyle style = ApiStyle.AsyncApi)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        {
            displayName = $"api-{Guid.NewGuid():N}", description = "d",
            style, version = "1.0", specUrl = (string?)null, teamId
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ApiResponse>())!.Id;
    }

    [TestMethod]
    public async Task Put_creates_then_get_returns_spec()
    {
        var (client, teamId) = await AuthedMemberClientAsync();   // helper per fixture convention
        var apiId = await RegisterApiAsync(client, teamId);

        var put = await client.PutAsync($"/api/v1/catalog/apis/{apiId}/spec", Yaml("asyncapi: 3.0.0"));
        Assert.AreEqual(HttpStatusCode.Created, put.StatusCode);

        var get = await client.GetAsync($"/api/v1/catalog/apis/{apiId}/spec");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        Assert.AreEqual(ApiMediaType.ApplicationYaml, get.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("asyncapi: 3.0.0", await get.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task Put_twice_replaces_and_returns_204()
    {
        var (client, teamId) = await AuthedMemberClientAsync();
        var apiId = await RegisterApiAsync(client, teamId);
        await client.PutAsync($"/api/v1/catalog/apis/{apiId}/spec", Json("{}"));
        var second = await client.PutAsync($"/api/v1/catalog/apis/{apiId}/spec", Json("{\"x\":1}"));
        Assert.AreEqual(HttpStatusCode.NoContent, second.StatusCode);
        var body = await (await client.GetAsync($"/api/v1/catalog/apis/{apiId}/spec")).Content.ReadAsStringAsync();
        Assert.AreEqual("{\"x\":1}", body);
    }

    [TestMethod]
    public async Task Get_absent_spec_is_404()
    {
        var (client, teamId) = await AuthedMemberClientAsync();
        var apiId = await RegisterApiAsync(client, teamId);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/catalog/apis/{apiId}/spec")).StatusCode);
    }

    [TestMethod]
    public async Task Put_unknown_api_is_404()
    {
        var (client, _) = await AuthedMemberClientAsync();
        var put = await client.PutAsync($"/api/v1/catalog/apis/{Guid.NewGuid()}/spec", Json("{}"));
        Assert.AreEqual(HttpStatusCode.NotFound, put.StatusCode);
    }

    [TestMethod]
    public async Task Put_empty_body_is_400()
    {
        var (client, teamId) = await AuthedMemberClientAsync();
        var apiId = await RegisterApiAsync(client, teamId);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await client.PutAsync($"/api/v1/catalog/apis/{apiId}/spec", Json("   "))).StatusCode);
    }

    [TestMethod]
    public async Task Put_bad_media_type_is_415()
    {
        var (client, teamId) = await AuthedMemberClientAsync();
        var apiId = await RegisterApiAsync(client, teamId);
        var put = await client.PutAsync($"/api/v1/catalog/apis/{apiId}/spec",
            new StringContent("<wsdl/>", Encoding.UTF8, "text/xml"));
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, put.StatusCode);
    }

    [TestMethod]
    public async Task Put_oversized_body_is_400()
    {
        var (client, teamId) = await AuthedMemberClientAsync();
        var apiId = await RegisterApiAsync(client, teamId);
        var big = new string('x', ApiSpec.MaxContentBytes + 1);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await client.PutAsync($"/api/v1/catalog/apis/{apiId}/spec", Json(big))).StatusCode);
    }

    [TestMethod]
    public async Task Non_member_put_is_403()
    {
        var (owner, teamId) = await AuthedMemberClientAsync();
        var apiId = await RegisterApiAsync(owner, teamId);
        var outsider = await AuthedMemberClientOtherTeamAsync();   // member of a different team, not OrgAdmin
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await outsider.PutAsync($"/api/v1/catalog/apis/{apiId}/spec", Json("{}"))).StatusCode);
    }

    [TestMethod]
    public async Task Cross_tenant_cannot_read_or_write_spec()
    {
        var (tenantA, teamA) = await AuthedMemberClientAsync();
        var apiId = await RegisterApiAsync(tenantA, teamA);
        await tenantA.PutAsync($"/api/v1/catalog/apis/{apiId}/spec", Json("{}"));

        var tenantB = await AuthedMemberClientOtherTenantAsync();
        Assert.AreEqual(HttpStatusCode.NotFound, (await tenantB.GetAsync($"/api/v1/catalog/apis/{apiId}/spec")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await tenantB.PutAsync($"/api/v1/catalog/apis/{apiId}/spec", Json("{}"))).StatusCode);
    }

    [TestMethod]
    public async Task HasSpec_flips_true_after_put()
    {
        var (client, teamId) = await AuthedMemberClientAsync();
        var apiId = await RegisterApiAsync(client, teamId);
        var before = await (await client.GetAsync($"/api/v1/catalog/apis/{apiId}")).Content.ReadFromJsonAsync<ApiResponse>();
        Assert.IsFalse(before!.HasSpec);
        await client.PutAsync($"/api/v1/catalog/apis/{apiId}/spec", Json("{}"));
        var after = await (await client.GetAsync($"/api/v1/catalog/apis/{apiId}")).Content.ReadFromJsonAsync<ApiResponse>();
        Assert.IsTrue(after!.HasSpec);
    }
}
```

> **Note for implementer:** use the exact fixture helper names `RegisterApiTests`/`CatalogPermissionMatrixTests` already use for authed clients + team seeding (`AuthedMemberClientAsync`, cross-tenant/other-team helpers may have different names — grep the fixture base and reuse verbatim; do not invent new helpers).

- [ ] **Step 2: Run — expect FAIL** (routes not mapped → 404/405)

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter ApiSpecTests"
```

- [ ] **Step 3: Add the delegates + membership helper** to `CatalogEndpointDelegates.cs`. Add a `LoadAndAuthorizeApiAsync` mirroring `LoadAndAuthorizeApplicationAsync` (load API by id → 404, then run `ApplicationTeamScoped` policy against the API's team via a `TargetTeam` wrapper), and the two spec delegates:

```csharp
private static async Task<(Api Api, IResult? Forbidden)?> LoadAndAuthorizeApiAsync(
    Guid id, CatalogDbContext db, IAuthorizationService auth, ClaimsPrincipal user, CancellationToken ct)
{
    var api = await db.Apis.FirstOrDefaultAsync(ApiSortSpecs.IdEquals(id), ct);
    if (api is null) return null;                          // caller maps to 404
    var forbidden = await AuthorizeTargetTeamAsync(auth, user, api.TeamId);
    return (api, forbidden);                               // forbidden != null ⇒ 403
}

internal static async Task<IResult> UpsertApiSpecAsync(
    Guid id,
    HttpRequest request,
    UpsertApiSpecHandler handler,
    CatalogDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IAuthorizationService auth,
    ClaimsPrincipal caller,
    IAuditWriter audit,
    CancellationToken ct)
{
    // Content-Type gate first (cheap, no body read).
    var mediaType = request.ContentType?.Split(';')[0].Trim();
    if (!ApiMediaType.IsAllowed(mediaType))
        return Results.Problem(
            title: "Unsupported spec media type",
            detail: "Content-Type must be application/json or application/yaml.",
            statusCode: StatusCodes.Status415UnsupportedMediaType);

    // Early cap on declared length; the read below re-caps for chunked bodies.
    if (request.ContentLength is > ApiSpec.MaxContentBytes)
        return SpecTooLarge();

    var loaded = await LoadAndAuthorizeApiAsync(id, db, auth, caller, ct);
    if (loaded is null) return EndpointResultExtensions.ApiNotFound();
    if (loaded.Value.Forbidden is { } forbidden) return forbidden;

    string content;
    using (var reader = new StreamReader(request.Body, Encoding.UTF8))
    {
        // Read with a hard ceiling so a chunked body cannot exceed the cap.
        var buffer = await ReadCappedAsync(reader, ApiSpec.MaxContentBytes, ct);
        if (buffer is null) return SpecTooLarge();
        content = buffer;
    }

    try
    {
        var created = await handler.Handle(
            new UpsertApiSpecCommand(id, content, mediaType!), db, tenant, currentUser, audit, ct);
        return created
            ? Results.Created($"/api/v1/catalog/apis/{id}/spec", null)
            : Results.NoContent();
    }
    catch (ArgumentException ex)   // empty content etc. from ApiSpec validation
    {
        return Results.Problem(title: "Invalid spec", detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
}

internal static async Task<IResult> GetApiSpecAsync(
    Guid id, GetApiSpecHandler handler, CatalogDbContext db, CancellationToken ct)
{
    var spec = await handler.Handle(new GetApiSpecQuery(id), db, ct);
    return spec is null
        ? EndpointResultExtensions.ApiNotFound()
        : Results.Text(spec.Value.Content, spec.Value.MediaType);
}

private static IResult SpecTooLarge()
    => Results.Problem(title: "Spec too large",
        detail: $"Spec content must be <= {ApiSpec.MaxContentBytes} bytes.",
        statusCode: StatusCodes.Status400BadRequest);

// Reads up to maxBytes+1 chars; returns null if the cap is exceeded (mapped to 400).
private static async Task<string?> ReadCappedAsync(StreamReader reader, int maxBytes, CancellationToken ct)
{
    var sb = new StringBuilder();
    var chunk = new char[8192];
    int read;
    while ((read = await reader.ReadAsync(chunk, ct)) > 0)
    {
        sb.Append(chunk, 0, read);
        if (Encoding.UTF8.GetByteCount(sb.ToString()) > maxBytes) return null;
    }
    return sb.ToString();
}
```

> Add `using System.Text;` and confirm `using Microsoft.AspNetCore.Http;` are present (mirror the file's existing usings).

- [ ] **Step 4: Map the routes** in `CatalogModule.cs`, after the `/apis` GET-list mapping (line ~204):

```csharp
tenant.MapPut("/apis/{id:guid}/spec", CatalogEndpointDelegates.UpsertApiSpecAsync)
      .RequireAuthorization(KartovaPermissions.CatalogApisRegister)
      .WithName("UpsertApiSpec")
      .Accepts<string>("application/json", "application/yaml")
      .Produces(StatusCodes.Status201Created)
      .Produces(StatusCodes.Status204NoContent)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .ProducesProblem(StatusCodes.Status403Forbidden)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
tenant.MapGet("/apis/{id:guid}/spec", CatalogEndpointDelegates.GetApiSpecAsync)
      .RequireAuthorization(KartovaPermissions.CatalogRead)
      .WithName("GetApiSpec")
      .Produces<string>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status404NotFound);
```

- [ ] **Step 5: Register the handlers** in `CatalogModule.RegisterServices` (next to the existing Api handler registrations):

```csharp
services.AddScoped<UpsertApiSpecHandler>();
services.AddScoped<GetApiSpecHandler>();
```

- [ ] **Step 6: Add permission-matrix rows** in `CatalogPermissionMatrixTests.cs` — one row per new endpoint (PUT `/apis/{id}/spec` → `catalog.apis.register`; GET `/apis/{id}/spec` → `catalog.read`), following the file's existing row shape.

- [ ] **Step 7: Run — expect PASS**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter ApiSpecTests"
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter CatalogPermissionMatrixTests"
```
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/
git commit -m "feat(catalog): PUT/GET /apis/{id}/spec sub-resource + real-seam tests"
```

---

## Task 8: Frontend forced touch (OpenAPI regen + `asyncApi` label)

**Files:**
- Modify: `web/src/features/catalog/schemas/registerApi.ts`
- Regenerate: `web/openapi-snapshot.json` + `web/src/generated/openapi*` (via codegen)
- Test: `web/src/features/catalog/schemas/__tests__/registerApi.test.ts` (extend)

**Interfaces:** `API_STYLES` gains `"asyncApi"`; `API_STYLE_LABEL` gains `asyncApi: "AsyncAPI"`. No UI in this slice.

> **Why forced:** `API_STYLE_LABEL: Record<ApiStyleValue, string>` is total; once the regenerated snapshot's `ApiResponse.style` union includes `"asyncApi"`, `tsc -b` fails until the key is added. Backend CI stays green, Frontend CI fails — so this is in-slice.

- [ ] **Step 1: Rebuild the API image / regenerate the snapshot.** The generated client is sourced from the live API (per project convention: `predev`/`prebuild` run `scripts/codegen.mjs`). Regenerate so `"asyncApi"` enters the union:

```bash
cd web && node scripts/codegen.mjs
```
Expected: `openapi-snapshot.json` diff adds `"asyncApi"` to `ApiResponse.style` (and `RegisterApiRequest.style`) enums + a `hasSpec` field on `ApiResponse`. (If codegen needs the live API, rebuild the API container first so the new enum value/endpoints are exposed — see the web-codegen memory.)

- [ ] **Step 2: Run tsc — expect FAIL** (missing label key)

```bash
cd web && npm run build
```
Expected: `tsc` error — `Property 'asyncApi' is missing in type … API_STYLE_LABEL`.

- [ ] **Step 3: Add the style value + label** in `registerApi.ts`:

```typescript
export const API_STYLES = ["rest", "grpc", "graphQL", "asyncApi"] as const satisfies readonly ApiStyleValue[];

export const API_STYLE_LABEL: Record<ApiStyleValue, string> = {
  rest: "REST",
  grpc: "gRPC",
  graphQL: "GraphQL",
  asyncApi: "AsyncAPI",
};
```

- [ ] **Step 4: Extend the schema test** — add an `asyncApi` case to `registerApi.test.ts` asserting it parses and has a label:

```typescript
it("accepts asyncApi style", () => {
  expect(registerApiSchema.shape.style.safeParse("asyncApi").success).toBe(true);
  expect(API_STYLE_LABEL.asyncApi).toBe("AsyncAPI");
});
```

- [ ] **Step 5: Run — expect PASS**

```bash
cd web && npm run build && npm test -- registerApi
```
Expected: build + tests green.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/schemas/registerApi.ts \
        web/src/features/catalog/schemas/__tests__/registerApi.test.ts \
        web/openapi-snapshot.json web/src/generated/
git commit -m "feat(web): expose asyncApi style (label + snapshot regen); no UI yet"
```

---

## Task 9: Checklist + DoD ledger + verification scaffolding

**Files:**
- Create: `docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage/dod.md` (copy template)
- Create: `docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage/gate-findings.yaml` (copy template)
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Scaffold the ledger**

```bash
mkdir -p docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage
cp docs/superpowers/templates/dod-ledger-template.md docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage/dod.md
cp docs/superpowers/templates/gate-findings-template.yaml docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage/gate-findings.yaml
```

- [ ] **Step 2: Fill the ledger header** (slice name, spec/plan paths, date) and leave gate rows pending — they're updated as gates run during the DoD phase, not here.

- [ ] **Step 3: Update `CHECKLIST.md`** — mark `E-02.F-03.S-02` shipped with a one-line summary (unified API entity + `AsyncApi` style + `catalog_api_specs` spec storage, ADR-0112 + ADR-0111 amendment; UI/versions/broker-edges deferred), and bump the Phase 1 counts.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage/ docs/product/CHECKLIST.md
git commit -m "docs(catalog): S-02 DoD ledger + checklist update"
```

---

## Definition of Done (run after Task 9, before any "complete" claim)

Per CLAUDE.md §DoD — eight always-blocking gates + gate-6 (**blocking**, Domain+Application logic changed). Fail-fast order; update `dod.md` per gate; log findings in `gate-findings.yaml`.

1. **Build** — `cmd //c "dotnet build Kartova.slnx"` (`TreatWarningsAsErrors=true`), 0 warnings.
2. **Per-task subagent reviews** — spec-compliance + code-quality (interleaved during dev; not skipped).
3. **Full test suite** — `cmd //c "dotnet test Kartova.slnx"`; wiring hits the real seam (real JWT + Postgres/RLS). (Watch the known Docker named-pipe flake — re-run the assembly in isolation before calling red.)
4. **Container build** — `scripts/ci-local.sh` `images` job (`docker compose build`) — proves the `AddApiSpec` migration applies in the `Kartova.Migrator` image.
5. **`/simplify`** against the branch diff.
6. **Mutation** — `/misc:mutation-sentinel` → `/misc:test-generator` on `ApiSpec`, `ApiMediaType`, `UpsertApiSpecHandler` (+ `HasSpec` paths); target ≥80%, document survivors.
7. **`/superpowers:requesting-code-review`** against the full branch diff.
8. **`/pr-review-toolkit:review-pr`** — run for real (do not fold into 7/9).
9. **`/deep-review`** against the branch diff (spec/plan/ADRs/tests).

**Pre-push CI mirror:** `scripts/ci-local.sh` (Release build+test, web image, helm/stryker) before opening the PR. **Terminal re-verify:** after gates 5–9 apply fixes, re-run build + full suite on the final commit.

---

## Self-Review (completed by plan author)

- **Spec coverage:** every spec §3 decision maps to a task — unified entity/AsyncApi (T2), text-in-Postgres/separate table (T3/T4), sub-resource endpoints (T7), raw-body dual-ingestion (T7 §Step 3), SpecUrl kept (untouched — no task needed), optional spec (T7 absent-spec test), reuse `catalog.apis.register` (T7 route), media allowlist + 5 MiB cap (T3/T7), `api.spec.updated` audit (T5/T6), `HasSpec` (T5/T6), no parsing (opaque text — T3), ADRs (T1), backend-only (UI absent by design). ADR-0111 amendment + ADR-0112 (T1).
- **Placeholder scan:** none — every code step carries real code; `<ts>` in the migration filename is an intentional EF-generated timestamp; fixture-helper names flagged as "grep-and-reuse verbatim" (T7 note) rather than invented.
- **Type consistency:** `ApiSpec.Create`/`Replace`, `UpsertApiSpecHandler.Handle → Task<bool>` (create=true), `GetApiSpecHandler.Handle → (string,string)?`, `ApiResponse.HasSpec`, `ApiMediaType.{ApplicationJson,ApplicationYaml,IsAllowed}`, `CatalogAuditActions.ApiSpecUpdated` — names match across tasks.
- **Impact analysis:** LSP/codelens unavailable → grep-grounded with explicit re-run-at-execution instruction (per the degradation clause); `ApiStyle` append confirmed additive (no switches); TS total-Record label is the one forced cross-stack edit (T8).
