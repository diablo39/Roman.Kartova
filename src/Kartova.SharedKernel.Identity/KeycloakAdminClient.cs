using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Duende.IdentityModel.Client;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kartova.SharedKernel.Identity;

internal sealed class KeycloakAdminClient(
    HttpClient http,
    IOptions<KeycloakAdminOptions> options,
    TokenClient tokenClient,
    ILogger<KeycloakAdminClient> logger) : IKeycloakAdminClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly string _realm = options.Value.Realm;

    public async Task<Guid> CreateUserAsync(CreateKeycloakUserRequest request, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/realms/{_realm}/users")
        {
            Content = JsonContent.Create(new
            {
                username = request.Email,    // KC requires explicit username unless the realm sets registrationEmailAsUsername=true; sending it makes the handler self-sufficient regardless of realm config.
                email = request.Email,
                firstName = request.FirstName,
                lastName = request.LastName,
                enabled = true,
                emailVerified = false,
                requiredActions = request.RequiredActions,
                attributes = new { tenant_id = new[] { request.TenantId } },
            }, options: JsonOpts),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict)
            throw new KeycloakAdminException(KeycloakAdminError.EmailAlreadyExists, "Email already exists in KeyCloak realm.");
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new KeycloakAdminException(KeycloakAdminError.Unauthorized, "Admin client unauthorized.");
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak create-user returned {(int)resp.StatusCode}.");

        var loc = resp.Headers.Location ?? throw new KeycloakAdminException(KeycloakAdminError.Unexpected, "Missing Location header on KeyCloak create-user response.");
        var idSegment = loc.Segments[^1].TrimEnd('/');
        if (!Guid.TryParse(idSegment, out var newId))
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, "KeyCloak create-user Location header did not contain a valid GUID segment.");
        logger.LogInformation("Created KeyCloak user {UserId} in realm {Realm}.", newId, _realm);
        return newId;
    }

    public async Task<KeycloakUser?> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/admin/realms/{_realm}/users/{userId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak get-user returned {(int)resp.StatusCode}.");

        var raw = await resp.Content.ReadFromJsonAsync<KeycloakUserRaw>(JsonOpts, ct)
                  ?? throw new KeycloakAdminException(KeycloakAdminError.Unexpected, "Empty KeyCloak get-user response.");
        return raw.ToDomain();
    }

    public async Task AssignRealmRoleAsync(Guid userId, string roleName, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);

        using var roleReq = new HttpRequestMessage(HttpMethod.Get, $"/admin/realms/{_realm}/roles/{Uri.EscapeDataString(roleName)}");
        roleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var roleResp = await http.SendAsync(roleReq, ct);
        if (roleResp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"Realm role '{roleName}' not found.");
        if (!roleResp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak get-role returned {(int)roleResp.StatusCode}.");
        var role = await roleResp.Content.ReadFromJsonAsync<RealmRole>(JsonOpts, ct)
                   ?? throw new KeycloakAdminException(KeycloakAdminError.Unexpected, "Empty KeyCloak get-role response.");

        using var assignReq = new HttpRequestMessage(HttpMethod.Post, $"/admin/realms/{_realm}/users/{userId}/role-mappings/realm")
        {
            Content = JsonContent.Create(new[] { role }, options: JsonOpts),
        };
        assignReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var assignResp = await http.SendAsync(assignReq, ct);
        if (assignResp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (!assignResp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak assign-role returned {(int)assignResp.StatusCode}.");
    }

    public async Task<IReadOnlyList<KeycloakUser>> SearchUsersAsync(string query, int limit, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        var uri = $"/admin/realms/{_realm}/users?search={Uri.EscapeDataString(query)}&max={limit}";
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak search-users returned {(int)resp.StatusCode}.");
        var raws = await resp.Content.ReadFromJsonAsync<List<KeycloakUserRaw>>(JsonOpts, ct) ?? new();
        return raws.Select(r => r.ToDomain()).ToList();
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/admin/realms/{_realm}/users/{userId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak delete-user returned {(int)resp.StatusCode}.");
    }

    public async Task SetPasswordAsync(Guid userId, string password, bool temporary, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/admin/realms/{_realm}/users/{userId}/reset-password")
        {
            Content = JsonContent.Create(new { type = "password", value = password, temporary }, options: JsonOpts),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new KeycloakAdminException(KeycloakAdminError.Unauthorized, "Admin client unauthorized.");
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak reset-password returned {(int)resp.StatusCode}.");
    }

    public async Task UpdateUserAsync(Guid userId, UpdateKeycloakUserRequest request, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/admin/realms/{_realm}/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                firstName = request.FirstName,
                lastName = request.LastName,
                emailVerified = request.EmailVerified,
                requiredActions = request.RequiredActions,
            }, options: JsonOpts),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new KeycloakAdminException(KeycloakAdminError.Unauthorized, "Admin client unauthorized.");
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak update-user returned {(int)resp.StatusCode}.");
    }

    public async Task ChangeRealmRoleAsync(Guid userId, string newRole, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);

        using var listReq = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/realms/{_realm}/users/{userId}/role-mappings/realm");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResp = await http.SendAsync(listReq, ct);
        if (listResp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (!listResp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak list-roles returned {(int)listResp.StatusCode}.");
        var current = await listResp.Content.ReadFromJsonAsync<RealmRole[]>(JsonOpts, ct) ?? [];

        var toRemove = KeycloakRoleChange
            .RolesToRemove(current.Select(r => (r.Id, r.Name)), newRole)
            .Select(t => new RealmRole(t.Id, t.Name))
            .ToArray();
        if (toRemove.Length > 0)
        {
            using var delReq = new HttpRequestMessage(HttpMethod.Delete,
                $"/admin/realms/{_realm}/users/{userId}/role-mappings/realm")
            { Content = JsonContent.Create(toRemove, options: JsonOpts) };
            delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var delResp = await http.SendAsync(delReq, ct);
            if (!delResp.IsSuccessStatusCode)
                throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak remove-roles returned {(int)delResp.StatusCode}.");
        }

        await AssignRealmRoleAsync(userId, newRole, ct);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var resp = await tokenClient.RequestClientCredentialsTokenAsync(cancellationToken: ct);
        if (resp.IsError || resp.AccessToken is null)
            throw new KeycloakAdminException(KeycloakAdminError.Unauthorized, $"Token fetch failed: {resp.Error}");
        return resp.AccessToken;
    }

    private sealed record KeycloakUserRaw(
        Guid Id, string Email, string? FirstName, string? LastName,
        bool Enabled, bool EmailVerified)
    {
        public KeycloakUser ToDomain() => new(
            Id, Email, FirstName, LastName, Enabled, EmailVerified);
    }

    private sealed record RealmRole(string Id, string Name);
}
