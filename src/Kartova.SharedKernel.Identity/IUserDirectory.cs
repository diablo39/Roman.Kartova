using Kartova.SharedKernel;

namespace Kartova.SharedKernel.Identity;

public interface IUserDirectory
{
    Task<UserDisplayInfo?> GetAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, UserDisplayInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
