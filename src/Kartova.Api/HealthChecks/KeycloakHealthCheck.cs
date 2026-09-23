using Kartova.SharedKernel.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Kartova.Api.HealthChecks;

/// <summary>
/// ADR-0060 dependency check for KeyCloak. No AspNetCore.HealthChecks.Keycloak
/// package is pinned in Directory.Packages.props (existence unverified) — a
/// hand-written check against the anonymous OIDC discovery endpoint avoids an
/// unverified NuGet dependency. Never throws — any failure (unreachable, a
/// malformed BaseUrl/Realm producing an invalid URI, etc.) is reported as
/// Unhealthy so a single misconfigured dependency never crashes the endpoint.
/// </summary>
public sealed class KeycloakHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakAdminOptions> options) : IHealthCheck
{
    public const string ClientName = "keycloak-health";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var url = $"{opts.BaseUrl.TrimEnd('/')}/realms/{opts.Realm}/.well-known/openid-configuration";

        try
        {
            var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("KeyCloak discovery endpoint reachable")
                : HealthCheckResult.Unhealthy($"KeyCloak returned HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("KeyCloak discovery endpoint unreachable", ex);
        }
    }
}
