using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_infrastructure",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_infrastructure", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_catalog_infrastructure_team",
                table: "catalog_infrastructure",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_infrastructure_tenant_id",
                table: "catalog_infrastructure",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_infrastructure_tenant_id_display_name",
                table: "catalog_infrastructure",
                columns: new[] { "tenant_id", "display_name" });

            // NOTE: RLS policy + GIN index are raw SQL — not captured by the model snapshot.
            migrationBuilder.Sql(@"
ALTER TABLE catalog_infrastructure ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_infrastructure FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_infrastructure
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

CREATE INDEX ix_catalog_infrastructure_attributes
  ON catalog_infrastructure USING gin (attributes jsonb_path_ops);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON catalog_infrastructure;
DROP INDEX IF EXISTS ix_catalog_infrastructure_attributes;
ALTER TABLE catalog_infrastructure DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "catalog_infrastructure");
        }
    }
}
