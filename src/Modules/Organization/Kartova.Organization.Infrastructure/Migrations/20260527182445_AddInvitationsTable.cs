using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<byte>(type: "smallint", nullable: false),
                    keycloak_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_invitations_tenant_status",
                table: "invitations",
                columns: new[] { "tenant_id", "status" });

            // status = 1 maps to InvitationStatus.Pending. The partial index enforces
            // "at-most-one Pending invitation per (tenant, lower(email))" while still
            // allowing re-invite after Revoked (2) / Accepted (3) / Expired (4).
            migrationBuilder.Sql(@"
ALTER TABLE invitations ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitations FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON invitations
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

CREATE INDEX idx_invitations_email_pending ON invitations(tenant_id, lower(email)) WHERE status = 1;
COMMENT ON INDEX idx_invitations_email_pending IS
  'Partial index on Pending invitations only: enforces ""one Pending invitation per (tenant, lower(email))"" while allowing re-invite after revoke (status=2) / accept (3) / expire (4). status=1 maps to Kartova.Organization.Domain.InvitationStatus.Pending.';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS idx_invitations_email_pending;
DROP POLICY IF EXISTS tenant_isolation ON invitations;
ALTER TABLE invitations DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "invitations");
        }
    }
}
