using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_type = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_display = table.Column<string>(type: "text", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    prev_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    row_hash = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_audit_log_tenant_target",
                table: "audit_log",
                columns: new[] { "tenant_id", "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "idx_audit_log_tenant_time",
                table: "audit_log",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_log_tenant_seq",
                table: "audit_log",
                columns: new[] { "tenant_id", "seq" },
                unique: true);

            migrationBuilder.Sql(@"
ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_log FORCE ROW LEVEL SECURITY;

-- Tenant isolation. USING gates SELECTs; WITH CHECK explicitly gates INSERTed rows so
-- the insert-tenant constraint is self-documenting and matches spec §4.
CREATE POLICY tenant_isolation ON audit_log
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- ADR-0018 insert-only: the app + bypass roles inherit SELECT,INSERT,UPDATE,DELETE from the
-- migrator's default privileges (docker/postgres/init.sql). Strip every mutating privilege so
-- an audit row can never be altered or removed by application code — the database, not app
-- discipline, is the guarantee. Guarded so the migration also applies in environments where a
-- role happens not to exist.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kartova_app') THEN
    REVOKE UPDATE, DELETE, TRUNCATE ON audit_log FROM kartova_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kartova_bypass_rls') THEN
    REVOKE UPDATE, DELETE, TRUNCATE ON audit_log FROM kartova_bypass_rls;
  END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON audit_log;
ALTER TABLE audit_log DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "audit_log");
        }
    }
}
