using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kartova.SharedKernel.Identity;

/// <summary>
/// Environment-aware validator for <see cref="KeycloakAdminOptions"/>. Rejects
/// <see cref="KeycloakAdminOptions.FrontendBaseUrl"/> values pointing at
/// <c>localhost</c> in any non-<c>Development</c> host environment — slice-9
/// carry-forward #15 (defence-in-depth alongside the
/// <c>OVERRIDE_VIA_ENV</c> guard on <c>AdminClientSecret</c>).
/// <para>
/// Localhost frontend URLs are valid for dev compose and integration tests,
/// but if they leak into staging/production configs the resulting invitation
/// emails would link recipients to dead loopback addresses — preferable to fail
/// fast at startup than to silently ship broken links.
/// </para>
/// </summary>
internal sealed class KeycloakAdminOptionsEnvValidator(IHostEnvironment env)
    : IValidateOptions<KeycloakAdminOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakAdminOptions options)
    {
        if (env.IsDevelopment() || env.IsEnvironment("Testing"))
        {
            return ValidateOptionsResult.Success;
        }

        var url = options.FrontendBaseUrl;
        if (url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                $"KartovaIdentity:Keycloak:FrontendBaseUrl = '{url}' targets localhost while " +
                $"the host environment is '{env.EnvironmentName}'. Invitation emails would " +
                "link recipients to a dead loopback address. Override the value via the " +
                "environment variable 'KartovaIdentity__Keycloak__FrontendBaseUrl' at deploy time.");
        }

        return ValidateOptionsResult.Success;
    }
}
