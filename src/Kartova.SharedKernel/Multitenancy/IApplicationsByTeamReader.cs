namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Read-only port that returns a summary of catalog applications currently
/// assigned to a given team within the active tenant scope. Used by the
/// Organization module's <c>GetTeamHandler</c> to populate the
/// <c>applications</c> field on the team detail response. Implemented by
/// the Catalog module so the Organization module never references Catalog
/// directly.
/// </summary>
public interface IApplicationsByTeamReader
{
    Task<IReadOnlyList<ApplicationByTeamSummary>> GetByTeamAsync(Guid teamId, CancellationToken ct);
}

/// <summary>
/// Cross-module port shape for a single application owned by a team.
/// <c>Lifecycle</c> is passed as a string to keep the Catalog enum out of
/// SharedKernel (ADR-0082 module boundary).
/// </summary>
public sealed record ApplicationByTeamSummary(Guid Id, string DisplayName, string Lifecycle);
