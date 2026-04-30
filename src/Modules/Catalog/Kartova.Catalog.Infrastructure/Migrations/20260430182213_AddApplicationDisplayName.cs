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
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "catalog_applications",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            // Backfill: existing rows get display_name = name so no row is left with
            // the empty-string placeholder default. New rows always supply a real value.
            // SET LOCAL satisfies the RLS policy (which evaluates current_setting for
            // every row scan) without permanently changing session state — the value
            // is rolled back at transaction end.  The placeholder UUID is arbitrary;
            // the UPDATE affects ALL rows regardless of tenant because the migrator
            // role owns the schema and runs inside a trusted maintenance connection.
            migrationBuilder.Sql(@"
SET LOCAL app.current_tenant_id = '00000000-0000-0000-0000-000000000000';
UPDATE catalog_applications SET display_name = name WHERE display_name = '';
");
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
