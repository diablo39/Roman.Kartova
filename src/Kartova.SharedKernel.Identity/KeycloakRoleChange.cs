using Kartova.SharedKernel.Multitenancy;

namespace Kartova.SharedKernel.Identity;

/// <summary>
/// Pure filter logic for <see cref="KeycloakAdminClient.ChangeRealmRoleAsync"/>:
/// determines which Kartova business realm roles should be removed from a user
/// before the new role is assigned. Extracted as an internal static helper so
/// the decision logic can be unit-tested without HTTP or TokenClient dependencies.
/// </summary>
internal static class KeycloakRoleChange
{
    /// <summary>
    /// Returns the subset of <paramref name="currentRoles"/> that are Kartova business
    /// realm roles (<see cref="KartovaRoles.All"/>) and differ from <paramref name="newRole"/>.
    /// </summary>
    public static IEnumerable<(string Id, string Name)> RolesToRemove(
        IEnumerable<(string Id, string Name)> currentRoles,
        string newRole) =>
        currentRoles.Where(r =>
            KartovaRoles.All.Contains(r.Name) &&
            !string.Equals(r.Name, newRole, StringComparison.Ordinal));
}
