using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationTeamId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "team_id",
                table: "catalog_applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_catalog_applications_team",
                table: "catalog_applications",
                column: "team_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_catalog_applications_team",
                table: "catalog_applications");

            migrationBuilder.DropColumn(
                name: "team_id",
                table: "catalog_applications");
        }
    }
}
