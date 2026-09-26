using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.AspNetCore;
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
        TrustSigner(services, JwtBearerDefaults.AuthenticationScheme, TestJwtSigner.Issuer, TestJwtSigner.Audience, signer);
        TrustSigner(services, PlatformAdminAuth.Scheme, TestJwtSigner.PlatformIssuer, TestJwtSigner.PlatformAudience, signer);
        return services;
    }

    private static void TrustSigner(IServiceCollection services, string scheme, string issuer, string audience, TestJwtSigner signer)
    {
        services.PostConfigure<JwtBearerOptions>(scheme, opts =>
        {
            opts.RequireHttpsMetadata = false;
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signer.PublicKey,
                ClockSkew = TimeSpan.FromSeconds(5),
            };
            opts.MapInboundClaims = false;
        });
    }
}
