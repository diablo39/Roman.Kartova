using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record AcceptInvitationRequest(
    string Token,
    /// <summary>
    /// SENSITIVE — must never be logged, traced, or included in diagnostic output.
    /// </summary>
    string Password,
    string DisplayName);
