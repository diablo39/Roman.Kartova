using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOneSystemPerComponentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // At-most-one System per component (ADR-0111 amended 2026-07-30). Partial index:
            // only PartOf edges are constrained; every other relationship type keeps its
            // unconstrained many-to-many cardinality. `type` is stored as a string
            // (EfRelationshipConfiguration.cs:50, HasConversion<string>()).
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ux_relationships_one_system
                ON relationships (tenant_id, source_kind, source_id)
                WHERE type = 'PartOf';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_relationships_one_system;");
        }
    }
}
