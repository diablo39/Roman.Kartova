namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Cross-module port that answers "does this team exist in the active tenant
/// scope?". Used by Catalog's <c>AssignApplicationTeamHandler</c> to validate
/// the target team before mutating <c>Application.TeamId</c>. Implemented by
/// the Organization module so Catalog never references Organization directly
/// (slice 8 / ADR-0098 §6). RLS narrows visibility to the current tenant — a
/// cross-tenant team id surfaces as "not exists" identically to an unknown id.
/// </summary>
public interface IOrganizationTeamExistenceChecker
{
    Task<bool> ExistsAsync(Guid teamId, CancellationToken ct);
}
