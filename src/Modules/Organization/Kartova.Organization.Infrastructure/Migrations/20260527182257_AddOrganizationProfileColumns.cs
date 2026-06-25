using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationProfileColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_time_zone",
                table: "organizations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "organizations",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "logo_bytes",
                table: "organizations",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_content_hash",
                table: "organizations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_mime_type",
                table: "organizations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(@"
ALTER TABLE organizations
  ADD CONSTRAINT chk_logo_complete CHECK (
    (logo_bytes IS NULL AND logo_mime_type IS NULL AND logo_content_hash IS NULL)
    OR (logo_bytes IS NOT NULL AND logo_mime_type IS NOT NULL AND logo_content_hash IS NOT NULL)
  );
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE organizations DROP CONSTRAINT IF EXISTS chk_logo_complete;");

            migrationBuilder.DropColumn(
                name: "default_time_zone",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "description",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "logo_bytes",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "logo_content_hash",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "logo_mime_type",
                table: "organizations");
        }
    }
}
