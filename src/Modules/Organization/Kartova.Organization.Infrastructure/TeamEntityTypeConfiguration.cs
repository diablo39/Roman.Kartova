using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Organization.Infrastructure;

internal sealed class TeamEntityTypeConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");
        builder.HasKey("_id");
        builder.Property<Guid>("_id").HasColumnName("id");
        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(t => t.Value, g => new TenantId(g));
        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(512);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("idx_teams_tenant");
        builder.Ignore(x => x.Id);
    }
}
