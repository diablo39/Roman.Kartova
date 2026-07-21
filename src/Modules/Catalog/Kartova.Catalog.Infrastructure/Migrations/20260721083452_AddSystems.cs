using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSystems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_systems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_systems", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_catalog_systems_team",
                table: "catalog_systems",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_systems_tenant_id",
                table: "catalog_systems",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_systems_tenant_id_display_name",
                table: "catalog_systems",
                columns: new[] { "tenant_id", "display_name" });

            migrationBuilder.Sql(@"
ALTER TABLE catalog_systems ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_systems FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_systems
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON catalog_systems;
ALTER TABLE catalog_systems DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "catalog_systems");
        }
    }
}
