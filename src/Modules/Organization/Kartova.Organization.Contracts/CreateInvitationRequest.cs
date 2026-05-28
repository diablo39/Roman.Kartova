using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Body shape for <c>POST /api/v1/organizations/invitations</c>. The handler
/// normalizes <see cref="Email"/> to lowercase + trimmed before duplicate
/// checks, and verifies <see cref="Role"/> against
/// <c>KartovaRoles.All</c> (Viewer / Member / TeamAdmin / OrgAdmin).
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record CreateInvitationRequest(string Email, string Role);
