namespace Kartova.Organization.Application;

public sealed record ChangeMemberRoleCommand(Guid UserId, string Role);

/// <summary>
/// Mutually-exclusive terminal outcomes of a change-role command. Modeled as an enum (not a
/// boolean-flag record) per ADR-0104: the operation returns no success payload, so an enum makes
/// illegal states (e.g. two flags true) unrepresentable and the endpoint switch exhaustive.
/// </summary>
public enum ChangeMemberRoleOutcome
{
    Success,
    NotFound,
    InvalidRole,
    LastOrgAdmin,
}
