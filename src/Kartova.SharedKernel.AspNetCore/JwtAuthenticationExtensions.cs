using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    /// </list>
    /// </summary>
    public static IServiceCollection AddKartovaJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration[AuthenticationConfigKeys.Authority];
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException($"{AuthenticationConfigKeys.Authority} not configured");
        }
        var audience = configuration[AuthenticationConfigKeys.Audience];
        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException($"{AuthenticationConfigKeys.Audience} not configured");
        }
        var metadataAddress = configuration[AuthenticationConfigKeys.MetadataAddress];
        var requireHttps = configuration.GetValue(AuthenticationConfigKeys.RequireHttpsMetadata, defaultValue: true);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
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
            });

        services.AddAuthorization();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }
}
