using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApiSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_api_specs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    media_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_api_specs", x => x.id);
                    table.ForeignKey(
                        name: "FK_catalog_api_specs_catalog_apis_api_id",
                        column: x => x.api_id,
                        principalTable: "catalog_apis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_api_specs_tenant_id",
                table: "catalog_api_specs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_catalog_api_specs_api_id",
                table: "catalog_api_specs",
                column: "api_id",
                unique: true);

            migrationBuilder.Sql(@"
ALTER TABLE catalog_api_specs ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_api_specs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_api_specs
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON catalog_api_specs;
ALTER TABLE catalog_api_specs DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "catalog_api_specs");
        }
    }
}
