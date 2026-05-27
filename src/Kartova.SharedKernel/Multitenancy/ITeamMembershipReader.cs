namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Read-only port that resolves a user's team memberships within the current
/// tenant scope. Implemented by the Organization module.
/// </summary>
public interface ITeamMembershipReader
{
    Task<IReadOnlyList<TeamMembershipInfo>> GetForUserAsync(Guid userId, CancellationToken ct);
}
