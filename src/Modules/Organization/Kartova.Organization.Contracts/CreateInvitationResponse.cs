using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// 201 Created envelope returned by <c>POST /api/v1/organizations/invitations</c>.
/// <see cref="InviteUrl"/> is the SPA accept entry point
/// <c>/accept-invitation?token=&lt;opaque-single-use-token&gt;</c>.
/// The token is single-use and expires after 7 days.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record CreateInvitationResponse(InvitationResponse Invitation, string InviteUrl);
