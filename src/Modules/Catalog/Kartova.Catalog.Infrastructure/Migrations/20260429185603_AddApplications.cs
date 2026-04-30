using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_applications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_applications_tenant_id",
                table: "catalog_applications",
                column: "tenant_id");

            migrationBuilder.Sql(@"
ALTER TABLE catalog_applications ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_applications FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_applications
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON catalog_applications;
ALTER TABLE catalog_applications DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "catalog_applications");
        }
    }
}
