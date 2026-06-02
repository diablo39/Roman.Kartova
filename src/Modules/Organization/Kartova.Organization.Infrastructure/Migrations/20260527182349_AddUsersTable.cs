using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    given_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    family_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_users_tenant",
                table: "users",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_users_tenant_email",
                table: "users",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.Sql(@"
ALTER TABLE users ENABLE ROW LEVEL SECURITY;
ALTER TABLE users FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON users
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- Trigram indexes are on lower(col), NOT the bare column: UserQueries.SearchAsync
-- filters lower(display_name)/lower(email) LIKE '%q%' (ToLower().Contains() for
-- InMemory-provider portability). A gin_trgm index on the bare column does NOT
-- match a lower(col) predicate, so the planner seq-scans (confirmed via
-- EXPLAIN ANALYZE 2026-06-01). lower(col) gin_trgm_ops makes both branches index-backed.
CREATE INDEX idx_users_displayname_trgm ON users USING gin (lower(display_name) gin_trgm_ops);
CREATE INDEX idx_users_email_trgm ON users USING gin (lower(email) gin_trgm_ops);
-- Kept for case-insensitive email equality/prefix lookups (trigram doesn't serve those efficiently).
CREATE INDEX idx_users_email_lower ON users(tenant_id, lower(email));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS idx_users_email_lower;
DROP INDEX IF EXISTS idx_users_email_trgm;
DROP INDEX IF EXISTS idx_users_displayname_trgm;
DROP POLICY IF EXISTS tenant_isolation ON users;
ALTER TABLE users DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
