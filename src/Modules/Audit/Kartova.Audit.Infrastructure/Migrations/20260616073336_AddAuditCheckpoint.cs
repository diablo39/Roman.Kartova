using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_checkpoint",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    row_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_checkpoint", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_audit_checkpoint_tenant_seq",
                table: "audit_checkpoint",
                columns: new[] { "tenant_id", "seq" },
                unique: true);

            // ADR-0105: a checkpoint inherits the exact trust model of the chain it attests to —
            // tenant-isolated and insert-only — so a checkpoint can never be altered or removed by
            // application code. Mirrors the audit_log treatment in InitialAuditLog.
            migrationBuilder.Sql(@"
ALTER TABLE audit_checkpoint ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_checkpoint FORCE ROW LEVEL SECURITY;

-- Tenant isolation. USING gates SELECTs; WITH CHECK gates INSERTed rows to the current tenant.
CREATE POLICY tenant_isolation ON audit_checkpoint
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- Insert-only: strip every mutating privilege so a checkpoint, once written, is immutable.
-- Guarded so the migration also applies where a role happens not to exist.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kartova_app') THEN
    REVOKE UPDATE, DELETE, TRUNCATE ON audit_checkpoint FROM kartova_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kartova_bypass_rls') THEN
    REVOKE UPDATE, DELETE, TRUNCATE ON audit_checkpoint FROM kartova_bypass_rls;
  END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON audit_checkpoint;
ALTER TABLE audit_checkpoint DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "audit_checkpoint");
        }
    }
}
