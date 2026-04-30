using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: add column nullable so the backfill can populate it before we lock NOT NULL.
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "catalog_applications",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // Step 2: disable FORCE RLS so the maintenance backfill runs as the table owner.
            // The migrator role owns catalog_applications but lacks BYPASSRLS; FORCE makes the
            // policy apply to the owner too. NO FORCE returns to the default (policy applies
            // only to non-owners), allowing this maintenance UPDATE to see every row regardless
            // of tenant. Re-enabled in Step 4 to preserve the slice-3 invariant for application
            // queries.
            migrationBuilder.Sql("ALTER TABLE catalog_applications NO FORCE ROW LEVEL SECURITY;");

            // Step 3: backfill — every existing Application gets display_name = name, satisfying
            // the new domain invariant (DisplayName non-empty).
            migrationBuilder.Sql("UPDATE catalog_applications SET display_name = name;");

            // Step 4: restore FORCE RLS to slice-3's stance.
            migrationBuilder.Sql("ALTER TABLE catalog_applications FORCE ROW LEVEL SECURITY;");

            // Step 5: lock NOT NULL now that every row has a value.
            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "catalog_applications",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_name",
                table: "catalog_applications");
        }
    }
}
