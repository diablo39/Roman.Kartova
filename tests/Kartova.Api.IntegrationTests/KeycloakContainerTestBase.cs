using System.Net.Http.Json;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;

namespace Kartova.Api.IntegrationTests;

/// <summary>
/// Convenience base class — exposes the assembly-scoped
/// <see cref="KeycloakAndPostgresContainers"/> (owned by
/// <see cref="IntegrationTestAssemblySetup"/>) as a protected static so derived
/// test classes can write <c>Containers.X</c> instead of the fully qualified
/// <c>IntegrationTestAssemblySetup.Containers.X</c>. The aggregate itself is
/// created exactly once per assembly run via <c>[AssemblyInitialize]</c>; this
/// base class adds no lifecycle.
/// </summary>
[TestClass]
public abstract class KeycloakContainerTestBase
{
    protected static KeycloakAndPostgresContainers Containers => IntegrationTestAssemblySetup.Containers;

    /// <summary>
    /// Mints a real access token from the shared realm via the OIDC Resource Owner
    /// Password Credentials grant. Shared so callers don't each reimplement the
    /// same token-endpoint/form/parsing shape.
    /// </summary>
    protected static Task<string> GetRealTokenAsync(string username, string password)
        => PasswordGrantAsync(Containers.KeycloakAuthority, "kartova-api", username, password);

    /// <summary>ADR-0118: real token from the operator realm via its dev/test password-grant client.</summary>
    protected static Task<string> GetRealPlatformTokenAsync(string username, string password)
        => PasswordGrantAsync(Containers.PlatformKeycloakAuthority, RealmSeedConstants.PlatformTestClientId, username, password);

    private static async Task<string> PasswordGrantAsync(string authority, string clientId, string username, string password)
    {
        using var oidc = new HttpClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid",
        });
        var tokenResp = await oidc.PostAsync($"{authority}/protocol/openid-connect/token", form);
        tokenResp.EnsureSuccessStatusCode();
        var payload = await tokenResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return payload!["access_token"].ToString()!;
    }

    /// <summary>
    /// ADR-0118: points the <c>PlatformAdmin</c> scheme at the live operator realm. Required by every
    /// host from Task 3 on (the platform config is fail-fast), so call it wherever the tenant
    /// <c>Authentication__*</c> env vars are set.
    /// </summary>
    protected static void SetPlatformAdminAuthEnv()
    {
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.PlatformAdminAuthority), Containers.PlatformKeycloakAuthority);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.PlatformAdminMetadataAddress),
            $"{Containers.PlatformKeycloakAuthority}/.well-known/openid-configuration");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.PlatformAdminAudience), RealmSeedConstants.PlatformApiAudience);
    }

    /// <summary>Converts a colon-separated config key to its env-var form (double underscore).</summary>
    protected static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
