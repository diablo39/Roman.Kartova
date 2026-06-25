using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamMembersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "team_members",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<byte>(type: "smallint", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_members", x => new { x.team_id, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "idx_team_members_user",
                table: "team_members",
                column: "user_id");

            migrationBuilder.Sql(@"
ALTER TABLE team_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE team_members FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON team_members
  USING (EXISTS (
    SELECT 1 FROM teams t
    WHERE t.id = team_members.team_id
      AND t.tenant_id = current_setting('app.current_tenant_id')::uuid
  ));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON team_members;
ALTER TABLE team_members DISABLE ROW LEVEL SECURITY;
");

            migrationBuilder.DropTable(
                name: "team_members");
        }
    }
}
