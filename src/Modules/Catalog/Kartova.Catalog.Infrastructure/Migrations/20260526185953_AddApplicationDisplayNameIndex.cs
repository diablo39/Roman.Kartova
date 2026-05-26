using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationDisplayNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_catalog_applications_tenant_id_display_name",
                table: "catalog_applications",
                columns: new[] { "tenant_id", "display_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_catalog_applications_tenant_id_display_name",
                table: "catalog_applications");
        }
    }
}
