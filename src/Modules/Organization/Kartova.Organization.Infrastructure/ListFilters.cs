namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wire-protocol sentinels shared by the Organization module's list endpoints.
/// </summary>
internal static class ListFilters
{
    /// <summary>
    /// The SPA-facing "All" tab value that explicitly disables a list endpoint's default
    /// filter — the role filter on the members directory and the status filter on invitations.
    /// Compared case-insensitively.
    /// </summary>
    public const string All = "all";
}
