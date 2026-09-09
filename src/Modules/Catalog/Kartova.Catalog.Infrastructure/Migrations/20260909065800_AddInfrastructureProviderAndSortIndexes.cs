using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInfrastructureProviderAndSortIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "catalog_infrastructure",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // ADR-0115 slice 2a (Task 7): 6 partial btree-expression indexes serving
            // VmSortSpecs' JSONB sort selectors (GET /catalog/infrastructure/vms). Each
            // expression is authored to be byte-identical to the SQL EF/Npgsql actually
            // emits for the matching SortSpec.KeySelector (captured via
            // CatalogDbContext.Infrastructure....ToQueryString() — see VmSortSpecs.cs remarks)
            // — otherwise Postgres seq-scans for that sort, which is purely a PERFORMANCE
            // regression: every sort appends the id tiebreaker, so cursor keyset paging stays
            // correct/deterministic across pages regardless of whether the index is used.
            // "WHERE type = 0" scopes each index to InfrastructureType.VirtualMachine rows only
            // (the smallint discriminator column), matching ListVmsHandler's fixed Type filter.
            migrationBuilder.Sql("""
                CREATE INDEX ix_catalog_infrastructure_vm_power_state
                    ON catalog_infrastructure (jsonb_extract_path_text(attributes, 'powerState'))
                    WHERE type = 0;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_catalog_infrastructure_vm_os
                    ON catalog_infrastructure (jsonb_extract_path_text(attributes, 'os'))
                    WHERE type = 0;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_catalog_infrastructure_vm_hostname
                    ON catalog_infrastructure (jsonb_extract_path_text(attributes, 'hostname'))
                    WHERE type = 0;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_catalog_infrastructure_vm_region
                    ON catalog_infrastructure (jsonb_extract_path_text(attributes, 'region'))
                    WHERE type = 0;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_catalog_infrastructure_vm_vcpu
                    ON catalog_infrastructure (((jsonb_extract_path_text(attributes, 'vcpu'))::int))
                    WHERE type = 0;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_catalog_infrastructure_vm_memory_gb
                    ON catalog_infrastructure (((jsonb_extract_path_text(attributes, 'memoryGb'))::int))
                    WHERE type = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_catalog_infrastructure_vm_memory_gb;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_catalog_infrastructure_vm_vcpu;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_catalog_infrastructure_vm_region;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_catalog_infrastructure_vm_hostname;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_catalog_infrastructure_vm_os;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_catalog_infrastructure_vm_power_state;");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "catalog_infrastructure");
        }
    }
}
