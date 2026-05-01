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
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // Step 2: disable FORCE RLS so the maintenance backfill runs as the table owner.
            // The migrator role owns catalog_applications but lacks BYPASSRLS (ADR-0090, slice-3
            // role-split). FORCE makes the policy apply to the owner too; NO FORCE returns to the
            // default so this maintenance UPDATE sees every row regardless of tenant. Re-enabled in
            // Step 4. ASSUMES migrator-role != bypass-role; revisit if that ever changes.
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
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
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
