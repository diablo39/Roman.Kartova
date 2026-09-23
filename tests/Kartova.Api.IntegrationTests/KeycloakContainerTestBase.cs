using System.Net.Http.Json;

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
    protected static async Task<string> GetRealTokenAsync(string username, string password)
    {
        using var oidc = new HttpClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "kartova-api",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid",
        });
        var tokenResp = await oidc.PostAsync($"{Containers.KeycloakAuthority}/protocol/openid-connect/token", form);
        tokenResp.EnsureSuccessStatusCode();
        var payload = await tokenResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return payload!["access_token"].ToString()!;
    }

    /// <summary>Converts a colon-separated config key to its env-var form (double underscore).</summary>
    protected static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
