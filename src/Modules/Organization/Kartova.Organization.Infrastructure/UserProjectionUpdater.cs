using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Kartova.Organization.Infrastructure;

public sealed class UserProjectionUpdater(
    TimeProvider clock,
    ILogger<UserProjectionUpdater> logger)
{
    public async Task UpsertAsync(
        OrganizationDbContext db,
        Guid userId,
        string email,
        string? givenName,
        string? familyName,
        TenantId tenantId,
        CancellationToken ct)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        var displayName = User.ComputeDisplayName(givenName, familyName, email);
        var now = clock.GetUtcNow();

        if (existing is null)
        {
            db.Users.Add(new User
            {
                Id = userId,
                TenantId = tenantId,
                Email = email,
                GivenName = givenName,
                FamilyName = familyName,
                DisplayName = displayName,
                LastSeenAt = now,
                CreatedAt = now,
            });
        }
        else
        {
            existing.Email = email;
            existing.GivenName = givenName;
            existing.FamilyName = familyName;
            existing.DisplayName = displayName;
            existing.LastSeenAt = now;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            // ADR-0100 / S2: the new partial UNIQUE index ix_users_tenant_email
            // on (tenant_id, lower(email)) catches the case where two distinct
            // KeyCloak `sub` claims share an email within a tenant — exactly
            // the invariant ADR-0100 forbids. Since the production path uses an
            // EF tracked entity (not ON CONFLICT (id)), reaching 23505 here means
            // the new constraint fired on tenant_id+lower(email), not on the PK.
            // Log + rethrow as a typed exception so ops can intervene; this is a
            // data-integrity event, never silently swallowed.
            logger.LogError(
                ex,
                "ADR-0100 violation: tenant {TenantId} already has a different user with email {Email}. "
                + "Investigate KeyCloak realm duplicateEmailsAllowed config + any out-of-band user imports.",
                tenantId.Value, email);
            throw new OneEmailPerTenantViolationException(tenantId.Value, email, ex);
        }
    }
}
