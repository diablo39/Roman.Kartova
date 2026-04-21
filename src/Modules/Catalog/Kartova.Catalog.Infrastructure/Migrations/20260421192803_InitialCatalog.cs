using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__kartova_metadata",
                columns: table => new
                {
                    module_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___kartova_metadata", x => x.module_name);
                });

            migrationBuilder.Sql("""
                INSERT INTO __kartova_metadata (module_name, schema_version, applied_at)
                VALUES ('catalog', 1, NOW())
                ON CONFLICT (module_name) DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM __kartova_metadata WHERE module_name = 'catalog';");

            migrationBuilder.DropTable(
                name: "__kartova_metadata");
        }
    }
}
