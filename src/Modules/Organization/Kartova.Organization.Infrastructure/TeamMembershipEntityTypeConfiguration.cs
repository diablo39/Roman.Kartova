using Kartova.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Organization.Infrastructure;

internal sealed class TeamMembershipEntityTypeConfiguration : IEntityTypeConfiguration<TeamMembership>
{
    public void Configure(EntityTypeBuilder<TeamMembership> builder)
    {
        builder.ToTable("team_members");
        builder.HasKey(x => new { x.TeamId, x.UserId });
        builder.Property(x => x.TeamId)
            .HasColumnName("team_id")
            .HasConversion(t => t.Value, g => new TeamId(g));
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Role).HasColumnName("role").HasConversion<byte>();
        builder.Property(x => x.AddedAt).HasColumnName("added_at");
        builder.HasIndex(x => x.UserId).HasDatabaseName("idx_team_members_user");

        // FK with cascade on delete — deleting a team removes its membership rows
        // (spec §4.2). Declared at the database level via raw SQL in the
        // AddTeamMembersForeignKeyCascade migration rather than via the EF model:
        // TeamMembership.TeamId is a TeamId value object (Guid converter) and
        // Team's principal key is a private `_id` Guid backing field, and EF's
        // type-compatibility check on HasOne/HasForeignKey rejects the mismatch.
        // The DB-level FK is the load-bearing guarantee; the EF side does not
        // need to know about a non-existent navigation (Team and TeamMembership
        // are separate aggregates per spec §4).
    }
}
