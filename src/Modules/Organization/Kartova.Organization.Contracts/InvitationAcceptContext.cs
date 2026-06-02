using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record InvitationAcceptContext(
    string OrgDisplayName,
    string InvitedByDisplayName,
    string Email,
    string DefaultDisplayName,
    string Role,
    DateTimeOffset ExpiresAt);
