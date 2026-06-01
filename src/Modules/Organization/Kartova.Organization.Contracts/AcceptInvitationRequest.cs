using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record AcceptInvitationRequest(string Token, string Password, string DisplayName);
