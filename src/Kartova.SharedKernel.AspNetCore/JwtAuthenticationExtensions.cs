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
        var authority = configuration["Authentication:Authority"]
            ?? throw new InvalidOperationException("Authentication:Authority not configured");
        var audience = configuration["Authentication:Audience"]
            ?? throw new InvalidOperationException("Authentication:Audience not configured");
        var metadataAddress = configuration["Authentication:MetadataAddress"];
        var requireHttps = configuration.GetValue("Authentication:RequireHttpsMetadata", defaultValue: true);

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
                options.MapInboundClaims = false;
            });

        services.AddAuthorization();

        return services;
    }
}
