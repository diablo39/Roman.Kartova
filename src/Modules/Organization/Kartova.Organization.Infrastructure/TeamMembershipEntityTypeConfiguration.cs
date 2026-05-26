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
    }
}
