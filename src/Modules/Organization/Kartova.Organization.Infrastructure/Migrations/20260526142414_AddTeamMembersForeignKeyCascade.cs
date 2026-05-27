using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamMembersForeignKeyCascade : Migration
    {
        // Slice-boundary review fix (slice 8) — spec §4.2 called for the
        // team_members.team_id -> teams.id FK with ON DELETE CASCADE, but the
        // initial AddTeamMembersTable migration (Task 10) only declared the
        // primary key. Without this FK, deleting a team leaves orphan rows in
        // team_members and the test fixture had to two-step the cleanup.
        //
        // Declared at the database level via raw SQL because the EF model
        // cannot bind the relationship: TeamMembership.TeamId is a TeamId
        // value object (with a Guid converter) and Team's principal key is a
        // private `_id` Guid backing field, and EF's HasOne/HasForeignKey
        // type-compatibility check rejects the mismatch.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL validates the new FK by scanning team_members, and that
            // scan runs through the RLS policy on team_members (the migrator
            // role owns the table and FORCE ROW LEVEL SECURITY makes the owner
            // subject to RLS too). The tenant_isolation policy reads
            // current_setting('app.current_tenant_id'), which is unset in the
            // migrator session — without a placeholder the validation fails
            // with `42704: unrecognized configuration parameter`. Set a dummy
            // tenant for the duration of this migration's transaction; EF wraps
            // each migration in a transaction so SET LOCAL is scoped correctly.
            migrationBuilder.Sql(@"
SET LOCAL app.current_tenant_id = '00000000-0000-0000-0000-000000000000';
ALTER TABLE team_members
  ADD CONSTRAINT fk_team_members_teams_team_id
  FOREIGN KEY (team_id) REFERENCES teams(id) ON DELETE CASCADE;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE team_members
  DROP CONSTRAINT IF EXISTS fk_team_members_teams_team_id;
");
        }
    }
}
