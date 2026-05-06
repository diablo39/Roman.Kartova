using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: add lifecycle column with default 1=Active. Default value
            // backfills every existing row to Active in a single ALTER — no
            // separate UPDATE pass required (per spec §3 Decision #15).
            migrationBuilder.AddColumn<short>(
                name: "lifecycle",
                table: "catalog_applications",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);   // Lifecycle.Active

            // Step 2: add sunset_date column (nullable — only set when transitioning
            // to Deprecated).
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sunset_date",
                table: "catalog_applications",
                type: "timestamptz",
                nullable: true);

            // Note: xmin is a Postgres system column and is NOT added here. EF Core
            // maps the `Version` shadow property to it automatically (see
            // EfApplicationConfiguration.HasColumnType("xid")). No DB schema change
            // for the concurrency token.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "sunset_date", table: "catalog_applications");
            migrationBuilder.DropColumn(name: "lifecycle",   table: "catalog_applications");
        }
    }
}
