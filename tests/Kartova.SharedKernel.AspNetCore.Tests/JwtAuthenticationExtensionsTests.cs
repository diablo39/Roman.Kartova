using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class JwtAuthenticationExtensionsTests
{
    // Use an HTTP authority throughout to represent the dev-compose scenario; tests that
    // don't specifically exercise the RequireHttpsMetadata flag pair it with an explicit
    // "false" so JwtBearer's PostConfigure guard does not reject the resolved options.
    private const string HttpAuthority = "http://keycloak:8080/realms/kartova";
    private const string HttpsAuthority = "https://keycloak.example.com/realms/kartova";
    private const string ValidAudience = "kartova-api";

    private static JwtBearerOptions BuildAndGetOptions(Dictionary<string, string?> config)
    {
        // Arrange
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddKartovaJwtAuth(cfg);
        var sp = services.BuildServiceProvider();

        // Assert (caller completes)
        return sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
                 .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private static Dictionary<string, string?> MinimalValidConfig() => new()
    {
        ["Authentication:Authority"] = HttpAuthority,
        ["Authentication:Audience"] = ValidAudience,
        ["Authentication:RequireHttpsMetadata"] = "false"
    };

    [Fact]
    public void AddKartovaJwtAuth_WhenAuthorityMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Audience"] = ValidAudience
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        var act = () => services.AddKartovaJwtAuth(cfg);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Authority not configured*");
    }

    [Fact]
    public void AddKartovaJwtAuth_WhenAudienceMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = HttpAuthority
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        var act = () => services.AddKartovaJwtAuth(cfg);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Audience not configured*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddKartovaJwtAuth_WhenAuthorityBlank_ThrowsInvalidOperationException(string blank)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = blank,
                ["Authentication:Audience"] = ValidAudience
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddKartovaJwtAuth(cfg);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Authority not configured*");
    }

    [Fact]
    public void AddKartovaJwtAuth_WhenMetadataAddressAbsent_DerivesFromAuthority()
    {
        // Arrange
        var config = MinimalValidConfig();

        // Act
        var options = BuildAndGetOptions(config);

        // Assert
        // When MetadataAddress is not explicitly configured, AddKartovaJwtAuth leaves the
        // option unset; JwtBearerPostConfigureOptions then derives the discovery document
        // URL from Authority. Observable contract: resolved MetadataAddress points at
        // Authority's well-known OIDC configuration endpoint.
        options.MetadataAddress.Should().Be(
            HttpAuthority.TrimEnd('/') + "/.well-known/openid-configuration");
    }

    [Fact]
    public void AddKartovaJwtAuth_WhenMetadataAddressProvided_PropagatesToOptions()
    {
        // Arrange
        const string explicitMetadata = "http://keycloak:8080/realms/kartova/some-other-discovery-path";
        var config = MinimalValidConfig();
        config["Authentication:MetadataAddress"] = explicitMetadata;

        // Act
        var options = BuildAndGetOptions(config);

        // Assert
        // When provided explicitly, the configured value is propagated verbatim — it is
        // NOT overwritten by PostConfigure's Authority-derived default.
        options.MetadataAddress.Should().Be(explicitMetadata);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void AddKartovaJwtAuth_WhenMetadataAddressWhitespaceOrEmpty_FallsBackToAuthorityDerivedDefault(string metadataValue)
    {
        // Arrange
        var config = MinimalValidConfig();
        config["Authentication:MetadataAddress"] = metadataValue;

        // Act
        var options = BuildAndGetOptions(config);

        // Assert
        // Guard `!string.IsNullOrWhiteSpace(metadataAddress)` prevents empty/whitespace values
        // from being propagated. The MetadataAddress is then derived from Authority by
        // PostConfigure — same result as when the key is absent entirely.
        options.MetadataAddress.Should().Be(
            HttpAuthority.TrimEnd('/') + "/.well-known/openid-configuration");
    }

    [Fact]
    public void AddKartovaJwtAuth_WhenRequireHttpsMetadataAbsent_DefaultsToTrue()
    {
        // Arrange
        // Use an HTTPS authority so JwtBearer's PostConfigure guard is satisfied when
        // RequireHttpsMetadata is left at its default (true).
        var config = new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = HttpsAuthority,
            ["Authentication:Audience"] = ValidAudience
        };

        // Act
        var options = BuildAndGetOptions(config);

        // Assert
        // Secure-by-default: when the flag is not configured, HTTPS for metadata discovery
        // must be required. Opting out is an explicit dev-only configuration.
        options.RequireHttpsMetadata.Should().BeTrue();
    }

    [Fact]
    public void AddKartovaJwtAuth_WhenRequireHttpsMetadataExplicitlyFalse_PropagatesFalse()
    {
        // Arrange
        var config = new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = HttpAuthority,
            ["Authentication:Audience"] = ValidAudience,
            ["Authentication:RequireHttpsMetadata"] = "false"
        };

        // Act
        var options = BuildAndGetOptions(config);

        // Assert
        options.RequireHttpsMetadata.Should().BeFalse();
    }

    [Fact]
    public void AddKartovaJwtAuth_SetsMapInboundClaimsFalse()
    {
        // Arrange
        var config = MinimalValidConfig();

        // Act
        var options = BuildAndGetOptions(config);

        // Assert
        // MapInboundClaims=false preserves original claim names from the JWT (e.g. "sub",
        // "aud") rather than transforming to the legacy Microsoft-style URIs. This is
        // required for downstream policy/authorization code that reads standard OIDC claims.
        options.MapInboundClaims.Should().BeFalse();
    }

    [Fact]
    public void AddKartovaJwtAuth_ConfiguresTokenValidationParametersAndAuthorityAndAudience()
    {
        // Arrange
        var config = MinimalValidConfig();

        // Act
        var options = BuildAndGetOptions(config);

        // Assert
        options.Authority.Should().Be(HttpAuthority);
        options.Audience.Should().Be(ValidAudience);
        options.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        options.TokenValidationParameters.ValidateLifetime.Should().BeTrue();
    }

    [Fact]
    public void AddKartovaJwtAuth_RegistersAuthenticationAndAuthorizationServices()
    {
        // Arrange
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(MinimalValidConfig())
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddKartovaJwtAuth(cfg);
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<IAuthenticationService>().Should().NotBeNull(
            "AddAuthentication must register IAuthenticationService in the DI container");
        sp.GetService<IAuthorizationService>().Should().NotBeNull(
            "AddAuthorization must register IAuthorizationService in the DI container");
    }

    [Fact]
    public void AddKartovaJwtAuth_ReturnsSameServiceCollectionForChaining()
    {
        // Arrange
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(MinimalValidConfig())
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        var result = services.AddKartovaJwtAuth(cfg);

        // Assert
        result.Should().BeSameAs(services);
    }
}
