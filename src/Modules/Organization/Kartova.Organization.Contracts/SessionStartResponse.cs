using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Response shape for <c>POST /api/v1/auth/session</c> — single-shot post-login
/// payload returned by the session bootstrap endpoint (slice 9 spec §6.7 / §9.8).
/// Carries everything the SPA needs to hydrate a fresh session in one round-trip:
/// caller identity, realm role + permission set, team memberships, the current
/// tenant's org profile, and (optionally) the just-accepted invitation payload
/// used to trigger the one-time welcome screen.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record SessionStartResponse(
    UserDisplayInfo Me,
    string Role,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<MeTeamMembership> Teams,
    OrgProfileResponse Organization,
    AcceptedInvitationInfo? AcceptedInvitation);
