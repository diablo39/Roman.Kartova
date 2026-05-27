namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Read-only port that returns the number of catalog applications currently
/// assigned to a given team within the active tenant scope. Used by the
/// Organization module's <c>DeleteTeamHandler</c> to block deletion when
/// applications still reference the team. Implemented by the Catalog module.
/// </summary>
public interface IApplicationCountByTeamReader
{
    Task<int> CountForTeamAsync(Guid teamId, CancellationToken ct);
}
