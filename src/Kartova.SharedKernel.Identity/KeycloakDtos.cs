using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Identity;

[ExcludeFromCodeCoverage]
public sealed record CreateKeycloakUserRequest(
    string Email,
    string? FirstName,
    string? LastName,
    string TenantId,
    IReadOnlyList<string> RequiredActions);

[ExcludeFromCodeCoverage]
public sealed record KeycloakUser(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    bool Enabled,
    bool EmailVerified,
    string? TenantId);
