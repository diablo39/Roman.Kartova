namespace Kartova.SharedKernel.Identity;

public interface IKeycloakAdminClient
{
    Task<Guid> CreateUserAsync(CreateKeycloakUserRequest request, CancellationToken ct);
    Task<KeycloakUser?> GetUserAsync(Guid userId, CancellationToken ct);
    Task AssignRealmRoleAsync(Guid userId, string roleName, CancellationToken ct);
    Task<IReadOnlyList<KeycloakUser>> SearchUsersAsync(string query, int limit, CancellationToken ct);
    Task DeleteUserAsync(Guid userId, CancellationToken ct);
}
