using Kartova.SharedKernel.AspNetCore.AuthorizationHandlers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Wires JwtBearer against KeyCloak using configuration section "Authentication":
    /// <list type="bullet">
    ///  <item><c>Authority</c> — OIDC issuer, e.g. http://keycloak:8080/realms/kartova</item>
    ///  <item><c>MetadataAddress</c> — discovery document URL (optional, derived from Authority if absent)</item>
    ///  <item><c>Audience</c> — expected <c>aud</c> claim, typically client id</item>
    ///  <item><c>RequireHttpsMetadata</c> — true in prod, false in dev docker-compose</item>
    ///  <item><c>PlatformAdmin:Authority</c> — ADR-0118 operator-realm OIDC issuer, e.g. http://keycloak:8080/realms/kartova-platform</item>
    ///  <item><c>PlatformAdmin:MetadataAddress</c> — operator-realm discovery document URL (optional, derived from PlatformAdmin:Authority if absent)</item>
    ///  <item><c>PlatformAdmin:Audience</c> — expected <c>aud</c> claim on operator-realm tokens</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddKartovaJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = Required(configuration, AuthenticationConfigKeys.Authority);
        var audience = Required(configuration, AuthenticationConfigKeys.Audience);
        var metadataAddress = configuration[AuthenticationConfigKeys.MetadataAddress];
        var platformAuthority = Required(configuration, AuthenticationConfigKeys.PlatformAdminAuthority);
        var platformAudience = Required(configuration, AuthenticationConfigKeys.PlatformAdminAudience);
        var platformMetadataAddress = configuration[AuthenticationConfigKeys.PlatformAdminMetadataAddress];
        var requireHttps = configuration.GetValue(AuthenticationConfigKeys.RequireHttpsMetadata, defaultValue: true);

        // Default scheme = tenant realm. PlatformAdmin = operator realm (ADR-0118); it is never the
        // default, so it only runs where a policy names it (PlatformAdminAuth.Policy).
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o => ConfigureBearer(o, authority, metadataAddress, audience, requireHttps))
            .AddJwtBearer(PlatformAdminAuth.Scheme, o => ConfigureBearer(o, platformAuthority, platformMetadataAddress, platformAudience, requireHttps));

        // mutation-survivor: AddAuthorizationBuilder() already registers the core authorization services; this AddAuthorization() call is kept for explicit API-surface intent. Mutation tooling reports this as a survivor because removing it doesn't change observable behaviour for our tests.
        services.AddAuthorization();
        services.AddAuthorizationBuilder()
                .AddKartovaPermissionPolicies()
                .AddKartovaResourcePolicies()
                .AddPlatformAdminPolicy();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<IAuthorizationHandler, ApplicationTeamScopedHandler>();
        services.AddScoped<IAuthorizationHandler, TeamAdminOfThisHandler>();

        return services;
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} not configured");
        }
        return value;
    }

    private static void ConfigureBearer(
        JwtBearerOptions options, string authority, string? metadataAddress, string audience, bool requireHttps)
    {
        options.Authority = authority;
        if (!string.IsNullOrWhiteSpace(metadataAddress))
        {
            options.MetadataAddress = metadataAddress;
        }
        options.Audience = audience;
        options.RequireHttpsMetadata = requireHttps;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        // Tighten the default 5-minute ClockSkew. ADR-0007 short-lived tokens
        // would otherwise be honored well past their nominal expiry.
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
        options.MapInboundClaims = false;
    }
}
