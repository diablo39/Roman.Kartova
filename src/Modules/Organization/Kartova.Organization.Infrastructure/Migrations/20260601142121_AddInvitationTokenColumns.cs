using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Organization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationTokenColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "credential_set_at",
                table: "invitations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token_hash",
                table: "invitations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_invitations_token_hash",
                table: "invitations",
                column: "token_hash",
                unique: true,
                filter: "token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_invitations_token_hash",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "credential_set_at",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "token_hash",
                table: "invitations");
        }
    }
}
