using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Application;

/// <summary>
/// Assign (or unassign) the team that owns a catalog application. <c>TeamId</c>
/// is nullable: <c>null</c> means "unassign". Slice 8 / ADR-0098 §6.
/// </summary>
public sealed record AssignApplicationTeamCommand(Guid Id, Guid? TeamId);

/// <summary>
/// Result envelope for <see cref="AssignApplicationTeamCommand"/>. Three terminal
/// states: success (with the mutated domain app), not-found (RLS-hidden or
/// deleted between pre-load and handler reload — same defensive 404 path as
/// <c>UpdateTeamAsync</c>), and invalid-team (target team does not exist in the
/// current tenant — 422 per spec §6.4). The domain alias avoids the
/// <c>Application</c> ↔ <c>Kartova.Catalog.Application</c> namespace clash.
/// </summary>
public sealed record AssignApplicationTeamResult(
    bool IsSuccess,
    bool IsNotFound,
    bool IsInvalidTeam,
    DomainApplication? App)
{
    public static AssignApplicationTeamResult NotFound => new(false, true, false, null);
    public static AssignApplicationTeamResult InvalidTeam => new(false, false, true, null);
    public static AssignApplicationTeamResult Success(DomainApplication app) =>
        new(true, false, false, app);
}
