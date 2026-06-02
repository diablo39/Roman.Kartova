using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <summary>
    /// Adds a UNIQUE index on <c>users (tenant_id, lower(email))</c> enforcing
    /// ADR-0100's "one email = one tenant" invariant at the database level —
    /// slice-9 carry-forward S2.
    /// <para>
    /// The pre-existing <c>ux_users_tenant_email</c> covers
    /// <c>(tenant_id, email)</c> verbatim, which is case-sensitive. Since
    /// <c>CreateInvitationHandler</c> lower-cases at insert time the practical
    /// risk is small, but an out-of-band insert path (raw SQL, projection
    /// reconstruction, manual data import) could bypass that normalization.
    /// The functional UNIQUE on <c>lower(email)</c> closes that gap and matches
    /// the case-insensitive uniqueness model used by the invitations partial
    /// index (<c>idx_invitations_email_pending</c>), keeping the two tables in
    /// the same posture for the same invariant.
    /// </para>
    /// <para>
    /// Schema-only change: no data backfill required (production data is
    /// already lower-cased at write time, so no row collides). Down() drops
    /// the index, restoring the prior state exactly.
    /// </para>
    /// </summary>
    public partial class AddUsersTenantEmailUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ix_users_tenant_email ON users (tenant_id, lower(email));
COMMENT ON INDEX ix_users_tenant_email IS
  'Functional UNIQUE index on users(tenant_id, lower(email)) enforcing ADR-0100 "
                + @"""strict one email per tenant"" at the database level. Closes the bypass-normalization gap "
                + @"left by the case-sensitive UNIQUE on (tenant_id, email): an out-of-band insert that skips the "
                + @"handler-level ToLowerInvariant would otherwise admit two casing variants of the same address "
                + @"into the same tenant. Slice-9 carry-forward S2.';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ix_users_tenant_email;
");
        }
    }
}
