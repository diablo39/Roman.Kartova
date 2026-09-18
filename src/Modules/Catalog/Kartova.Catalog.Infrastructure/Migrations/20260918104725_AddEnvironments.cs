using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_environments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    region = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    cluster = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    resource_details = table.Column<string>(type: "jsonb", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_environments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_environments_tenant_id",
                table: "catalog_environments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_catalog_environments_tenant_id_display_name",
                table: "catalog_environments",
                columns: new[] { "tenant_id", "display_name" },
                unique: true);

            migrationBuilder.Sql(@"
ALTER TABLE catalog_environments ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_environments FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_environments
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON catalog_environments;
ALTER TABLE catalog_environments DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "catalog_environments");
        }
    }
}
