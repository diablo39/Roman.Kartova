using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class UserProjectionUpdater(TimeProvider clock)
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
        await db.SaveChangesAsync(ct);
    }
}
