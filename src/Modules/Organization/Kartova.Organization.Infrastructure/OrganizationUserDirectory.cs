using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

internal sealed class OrganizationUserDirectory(OrganizationDbContext db) : IUserDirectory
{
    public async Task<UserDisplayInfo?> GetAsync(Guid userId, CancellationToken ct)
        => await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserDisplayInfo(u.Id, u.DisplayName, u.Email))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, UserDisplayInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, UserDisplayInfo>();
        var rows = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserDisplayInfo(u.Id, u.DisplayName, u.Email))
            .ToListAsync(ct);
        return rows.ToDictionary(u => u.Id);
    }
}
