using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kartova.Testing.Auth;

[ExcludeFromCodeCoverage]
public static class TestAuthenticationExtensions
{
    /// <summary>
    /// Replaces the real JWT bearer validation with one that trusts the given TestJwtSigner's
    /// public key. Use in integration-test WebApplicationFactory setup.
    /// </summary>
    public static IServiceCollection UseTestJwtSigner(this IServiceCollection services, TestJwtSigner signer)
    {
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
        {
            opts.RequireHttpsMetadata = false;
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = TestJwtSigner.Issuer,
                ValidateAudience = true,
                ValidAudience = TestJwtSigner.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signer.PublicKey,
                ClockSkew = TimeSpan.FromSeconds(5),
            };
            opts.MapInboundClaims = false;
        });
        return services;
    }
}
