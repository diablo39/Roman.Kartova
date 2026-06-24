using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "relationships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relationships", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_relationships_tenant_source",
                table: "relationships",
                columns: new[] { "tenant_id", "source_kind", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_relationships_tenant_target",
                table: "relationships",
                columns: new[] { "tenant_id", "target_kind", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ux_relationships_edge",
                table: "relationships",
                columns: new[] { "tenant_id", "source_kind", "source_id", "type", "target_kind", "target_id" },
                unique: true);

            migrationBuilder.Sql(@"
ALTER TABLE relationships ENABLE ROW LEVEL SECURITY;
ALTER TABLE relationships FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON relationships
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON relationships;
ALTER TABLE relationships DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "relationships");
        }
    }
}
