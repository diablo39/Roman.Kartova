using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnablePgTrgmExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // pg_trgm intentionally not dropped on Down — other slices may rely on it.
        }
    }
}
