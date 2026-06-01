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
public sealed record UpdateKeycloakUserRequest(
    string? FirstName,
    string? LastName,
    bool EmailVerified,
    IReadOnlyList<string> RequiredActions);

/// <summary>
/// Slim domain projection of a Keycloak user. Note: the <c>tenant_id</c> custom
/// attribute IS persisted server-side on user creation (see
/// <see cref="KeycloakAdminClient.CreateUserAsync"/>), but KC 26's
/// <c>GET /users/{id}</c> omits custom attributes by default — and consumers
/// here always read the canonical tenant id off their own DB row, never off
/// the KC representation. The field was therefore dropped to avoid a dead
/// surface that always came back null.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record KeycloakUser(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    bool Enabled,
    bool EmailVerified);
