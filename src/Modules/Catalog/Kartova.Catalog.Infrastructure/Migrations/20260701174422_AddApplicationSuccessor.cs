using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationSuccessor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "successor_application_id",
                table: "catalog_applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_applications_successor_application_id",
                table: "catalog_applications",
                column: "successor_application_id");

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_applications_catalog_applications_successor_applica~",
                table: "catalog_applications",
                column: "successor_application_id",
                principalTable: "catalog_applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_catalog_applications_catalog_applications_successor_applica~",
                table: "catalog_applications");

            migrationBuilder.DropIndex(
                name: "ix_catalog_applications_successor_application_id",
                table: "catalog_applications");

            migrationBuilder.DropColumn(
                name: "successor_application_id",
                table: "catalog_applications");
        }
    }
}
