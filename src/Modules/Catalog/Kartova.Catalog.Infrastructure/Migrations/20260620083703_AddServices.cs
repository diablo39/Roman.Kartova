using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    health = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    endpoints = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_services", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_catalog_services_team",
                table: "catalog_services",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_services_tenant_id",
                table: "catalog_services",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_services_tenant_id_display_name",
                table: "catalog_services",
                columns: new[] { "tenant_id", "display_name" });

            migrationBuilder.Sql(@"
ALTER TABLE catalog_services ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_services FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_services
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON catalog_services;
ALTER TABLE catalog_services DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "catalog_services");
        }
    }
}
