using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <summary>
    /// Promotes the partial Pending-invitations index introduced in
    /// <c>AddInvitationsTable</c> from a regular index to a UNIQUE index, closing
    /// slice-9 carry-forward #10 (the D5 race-condition gap): the previous
    /// application-level pre-check in <c>CreateInvitationHandler</c> could allow a
    /// second Pending invitation for the same (tenant_id, lower(email)) pair to
    /// commit between the AnyAsync check and SaveChangesAsync. Promoting the
    /// partial index to UNIQUE moves enforcement to the database — concurrent
    /// duplicate creates now race the index, the second commit surfaces a
    /// 23505 unique-violation, and PostgresUniqueViolationProblemMapper (or the
    /// handler's catch) turns it into the same 409 EmailAlreadyInvited contract.
    /// </summary>
    /// <remarks>
    /// Schema-only change: no data backfill, no FORCE-RLS toggle (the partial
    /// index already filters on <c>status = 1</c> / Pending). The Down() reverses
    /// the swap so a rollback restores the prior non-unique index exactly as it
    /// existed in <c>AddInvitationsTable</c>.
    /// </remarks>
    public partial class MakeInvitationsPendingIndexUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS idx_invitations_email_pending;
CREATE UNIQUE INDEX idx_invitations_email_pending ON invitations(tenant_id, lower(email)) WHERE status = 1;
COMMENT ON INDEX idx_invitations_email_pending IS
  'Partial UNIQUE index on Pending invitations only: enforces ""one Pending invitation per (tenant, lower(email))"" at the database level. Closes the race-condition gap left by the handler-level AnyAsync pre-check (slice-9 carry-forward #10). Re-invite after revoke (status=2) / accept (3) / expire (4) remains allowed because the WHERE clause filters them out. status=1 maps to Kartova.Organization.Domain.InvitationStatus.Pending.';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS idx_invitations_email_pending;
CREATE INDEX idx_invitations_email_pending ON invitations(tenant_id, lower(email)) WHERE status = 1;
COMMENT ON INDEX idx_invitations_email_pending IS
  'Partial index on Pending invitations only: enforces ""one Pending invitation per (tenant, lower(email))"" while allowing re-invite after revoke (status=2) / accept (3) / expire (4). status=1 maps to Kartova.Organization.Domain.InvitationStatus.Pending.';
");
        }
    }
}
