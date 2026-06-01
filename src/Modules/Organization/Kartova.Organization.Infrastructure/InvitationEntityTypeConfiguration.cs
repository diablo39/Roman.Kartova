using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Organization.Infrastructure;

internal sealed class InvitationEntityTypeConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable("invitations");

        // Backing-field strategy mirrors slice 8 (Team aggregate id).
        // Defensive: _id is always assigned in Invitation.Create — pin EF's
        // convention so it never silently treats the Guid PK as database-
        // generated regardless of provider defaults.
        b.Property<Guid>("_id").HasColumnName("id").ValueGeneratedNever();
        b.HasKey("_id");

        b.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(v => v.Value, v => new TenantId(v));
        b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(x => x.Role).HasColumnName("role").HasMaxLength(32).IsRequired();
        b.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id");
        b.Property(x => x.InvitedAt).HasColumnName("invited_at");
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<byte>();
        b.Property(x => x.KeycloakUserId).HasColumnName("keycloak_user_id");
        b.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        b.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        b.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
        b.Property(x => x.CredentialSetAt).HasColumnName("credential_set_at");

        // Global lookup key for the anonymous accept path; UNIQUE so a token maps to
        // at most one invitation. Partial (token_hash IS NOT NULL) so burned/legacy
        // NULL rows don't collide.
        b.HasIndex(x => x.TokenHash)
            .HasDatabaseName("idx_invitations_token_hash")
            .IsUnique()
            .HasFilter("token_hash IS NOT NULL");

        b.HasIndex(x => new { x.TenantId, x.Status }).HasDatabaseName("idx_invitations_tenant_status");
    }
}
