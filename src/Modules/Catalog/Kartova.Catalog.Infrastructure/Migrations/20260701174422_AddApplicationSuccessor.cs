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

            // catalog_applications runs FORCE ROW LEVEL SECURITY with a tenant_isolation policy
            // USING (tenant_id = current_setting('app.current_tenant_id')::uuid) — the STRICT form.
            // Adding this self-referential FK triggers a constraint-validation scan of the table;
            // under FORCE RLS that scan evaluates the strict current_setting, which is unset in the
            // migrator session, throwing 42704. Toggle FORCE off around the ADD CONSTRAINT (the same
            // owner-operation pattern DevSeed uses), then restore it. The new column is nullable with
            // no pre-existing rows, so no real validation is skipped. Runs inside the migration
            // transaction, so a failure rolls back the toggle too.
            migrationBuilder.Sql("ALTER TABLE catalog_applications NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.AddForeignKey(
                name: "FK_catalog_applications_catalog_applications_successor_applica~",
                table: "catalog_applications",
                column: "successor_application_id",
                principalTable: "catalog_applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("ALTER TABLE catalog_applications FORCE ROW LEVEL SECURITY;");
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
