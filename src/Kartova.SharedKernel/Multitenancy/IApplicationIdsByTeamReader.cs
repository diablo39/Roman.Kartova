namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Read-only port that returns the ids of catalog applications currently
/// assigned to a given team within the active tenant scope. Used by the
/// Organization module's <c>GetTeamHandler</c> to populate the
/// <c>applicationIds</c> field on the team detail response. Implemented by
/// the Catalog module so the Organization module never references Catalog
/// directly.
/// </summary>
public interface IApplicationIdsByTeamReader
{
    Task<IReadOnlyList<Guid>> GetIdsByTeamAsync(Guid teamId, CancellationToken ct);
}
