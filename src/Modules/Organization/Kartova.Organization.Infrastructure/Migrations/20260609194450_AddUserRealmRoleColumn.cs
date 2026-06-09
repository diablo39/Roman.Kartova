using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRealmRoleColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "realm_role",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Viewer");

            migrationBuilder.Sql(
                "CREATE INDEX idx_users_orgadmins ON users (tenant_id) WHERE realm_role = 'OrgAdmin';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_users_orgadmins;");
            migrationBuilder.DropColumn(name: "realm_role", table: "users");
        }
    }
}
