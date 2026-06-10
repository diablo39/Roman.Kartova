using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RealignApplicationOwnership : Migration
    {
        // Deterministic demo-team id seeded by Kartova.Migrator.DevSeed (DemoTeamId).
        // Used to backfill any teamless apps before team_id becomes NOT NULL
        // (ADR-0101: pre-production, no live tenants — see ADR-0103 backfill note).
        private const string DemoTeamId = "dddddddd-0001-0001-0001-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "owner_user_id",
                table: "catalog_applications",
                newName: "created_by_user_id");

            // Backfill any teamless apps to the seeded demo team (pre-production,
            // ADR-0101 — no live tenants). On a fresh DB this affects 0 rows (apps are
            // seeded AFTER migrations); included for correctness on any populated DB so
            // the SET NOT NULL below cannot fail.
            //
            // Disable FORCE RLS around the maintenance UPDATE so it runs as the table owner
            // across every tenant (the migrator role owns the table but lacks BYPASSRLS, and
            // the tenant_isolation policy reads current_setting('app.current_tenant_id'), which
            // is unset during a migration → 42704). Same pattern as AddApplicationDisplayName.
            migrationBuilder.Sql("ALTER TABLE catalog_applications NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                $"UPDATE catalog_applications SET team_id = '{DemoTeamId}' WHERE team_id IS NULL;");
            migrationBuilder.Sql("ALTER TABLE catalog_applications FORCE ROW LEVEL SECURITY;");

            migrationBuilder.AlterColumn<Guid>(
                name: "team_id",
                table: "catalog_applications",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "created_by_user_id",
                table: "catalog_applications",
                newName: "owner_user_id");

            migrationBuilder.AlterColumn<Guid>(
                name: "team_id",
                table: "catalog_applications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
