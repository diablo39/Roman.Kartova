using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// 201 Created envelope returned by <c>POST /api/v1/organizations/invitations</c>.
/// <see cref="InviteUrl"/> embeds the SPA accept-flow entry point (already
/// includes the <c>?invitation=1</c> hint the SPA reads on landing).
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record CreateInvitationResponse(InvitationResponse Invitation, string InviteUrl);
