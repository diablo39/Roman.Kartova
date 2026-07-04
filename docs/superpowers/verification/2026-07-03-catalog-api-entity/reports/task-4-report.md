# Task 4 Report — EF configuration + DbContext + migration (RLS)

## Files created/changed

- **Created:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApiConfiguration.cs`
- **Modified:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs`
  - Added `public DbSet<Kartova.Catalog.Domain.Api> Apis => Set<Kartova.Catalog.Domain.Api>();` after `Services`.
  - Added `modelBuilder.ApplyConfiguration(new EfApiConfiguration());` in `OnModelCreating`, after `EfServiceConfiguration` and before `EfRelationshipConfiguration`.
- **Created:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/20260703161759_AddApis.cs`
- **Created:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/20260703161759_AddApis.Designer.cs`
- **Modified:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/CatalogDbContextModelSnapshot.cs`

Commit: `442b85f` — `feat(catalog): catalog_apis table + EF mapping + RLS migration`

## Migration-generation route: EF tooling (not hand-authored)

`dotnet ef --version` reported `10.0.0` (global tool), so I used the real tooling rather than hand-authoring:

```
dotnet ef migrations add AddApis --project src\Modules\Catalog\Kartova.Catalog.Infrastructure --startup-project src\Kartova.Migrator
```

Output: `Build succeeded.` (with a benign "EF tools version 10.0.0 is older than runtime 10.0.2" notice) and `Done. To undo this action, use 'ef migrations remove'`. This generated `20260703161759_AddApis.cs`, `20260703161759_AddApis.Designer.cs`, and updated `CatalogDbContextModelSnapshot.cs` automatically — matching the plan's "typical" happy path, no hand-authoring needed.

## RLS SQL added (hand-added, EF does not emit it)

Appended to `Up` (after the three `CreateIndex` calls):

```sql
ALTER TABLE catalog_apis ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_apis FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_apis
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
```

Prepended to `Down` (before `DropTable`):

```sql
DROP POLICY IF EXISTS tenant_isolation ON catalog_apis;
ALTER TABLE catalog_apis DISABLE ROW LEVEL SECURITY;
```

Both blocks are verbatim from the plan's Task 4 §Step 4, and structurally mirror `20260620083703_AddServices.cs`.

## Build result

```
cmd //c "dotnet build src\Modules\Catalog\Kartova.Catalog.Infrastructure -v q"
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:03.55
```

(Run via the PowerShell tool rather than `cmd //c` since PowerShell was available directly; command and output are equivalent — no warnings, no errors.)

## Snapshot confirmation

`CatalogDbContextModelSnapshot.cs` now contains a full `Api` entity block (lines 26–93), table `catalog_apis`:

```csharp
modelBuilder.Entity("Kartova.Catalog.Domain.Api", b =>
    {
        b.Property<Guid>("_id")...HasColumnName("id");
        b.Property<DateTimeOffset>("CreatedAt")...HasColumnName("created_at");
        b.Property<Guid>("CreatedByUserId")...HasColumnName("created_by_user_id");
        b.Property<string>("Description")...HasMaxLength(4096)...HasColumnName("description");
        b.Property<string>("DisplayName")...HasMaxLength(128)...HasColumnName("display_name");
        b.Property<string>("SpecUrl")...HasMaxLength(2048)...HasColumnName("spec_url");
        b.Property<short>("Style")...HasColumnType("smallint")...HasColumnName("style");
        b.Property<Guid>("TeamId")...HasColumnName("team_id");
        b.Property<Guid>("TenantId")...HasColumnName("tenant_id");
        b.Property<string>("Version")...HasMaxLength(64)...HasColumnName("version");
        b.Property<uint>("Xmin")...HasColumnType("xid")...HasColumnName("xmin");

        b.HasKey("_id");

        b.HasIndex("TeamId").HasDatabaseName("idx_catalog_apis_team");
        b.HasIndex("TenantId").HasDatabaseName("ix_catalog_apis_tenant_id");
        b.HasIndex("TenantId", "DisplayName").HasDatabaseName("ix_catalog_apis_tenant_id_display_name");

        b.ToTable("catalog_apis", (string)null);
    });
```

All column names, max-lengths, types (smallint for Style, xid for Xmin), and index names match `EfApiConfiguration.cs` and the plan.

## Self-review findings

Reviewed the generated `Up`/`Down` migration and the configuration against the plan line-by-line:

- Column set and order in `CreateTable` matches plan: `id, tenant_id, display_name, description, style, version, spec_url, team_id, created_by_user_id, created_at, xmin`.
- `style` is `smallint`, not nullable — correct (`ApiStyle` is a required enum, `HasConversion<short>()`).
- `version` is `character varying(64)`, not nullable — correct.
- `spec_url` is `character varying(2048)`, **nullable** — correct (matches `SpecUrl` being `string?` on the domain).
- `xmin` is `uid`-typed `uint`, `rowVersion: true` — correct, matches `AddServices` pattern.
- Three indexes present with exact names from the plan: `idx_catalog_apis_team`, `ix_catalog_apis_tenant_id`, `ix_catalog_apis_tenant_id_display_name`.
- RLS SQL is character-for-character what the plan specified, and mirrors `AddServices`' RLS block (same policy name `tenant_isolation`, same `current_setting('app.current_tenant_id')::uuid` predicate).
- `Down` correctly drops the policy and disables RLS before `DropTable` — ordering matters (can't drop a table with policy statements referencing it out of order, and Postgres would actually allow either order since `DROP TABLE` cascades policies, but doing it explicitly mirrors `AddServices` and is safer/clearer).
- No foreign key was generated — consistent with the plan's "no FK this slice" note.
- `EfApiConfiguration.cs` field-backed `_id` pattern, `Ignore(x => x.Id)`, and `HasKey(IdFieldName)` match `EfServiceConfiguration.cs`'s established pattern (verified by inspection of that sibling file).
- `CatalogDbContext.cs`: `Apis` DbSet added directly after `Services` (per plan step 2); `ApplyConfiguration(new EfApiConfiguration())` added directly after `EfServiceConfiguration()` (per plan step 2, "after `ApplyConfiguration(new EfServiceConfiguration())`").

No issues found; no fixes needed.

## Concerns / deviations from plan

- None. The EF-tooling route worked cleanly (global `dotnet ef` 10.0.0 present), so no hand-authoring was required.
- Minor: `dotnet ef` emitted a non-fatal "tools version 10.0.0 is older than runtime 10.0.2" advisory — informational only, did not affect the generated migration's correctness (verified against `AddServices` structure and the plan's exact expected SQL/columns).
- Generated migration/Designer/snapshot files initially had CRLF line endings on disk (as emitted by the `dotnet ef` CLI on Windows); `git add` normalized them to LF in the index per `.gitattributes eol=lf` (git printed the expected "CRLF will be replaced by LF" warnings) — the committed blobs are LF, consistent with repo convention.
- Did not run/apply the migration against a live DB per the task instructions (out of scope — applied later by the integration-test fixture in Task 7/8).
