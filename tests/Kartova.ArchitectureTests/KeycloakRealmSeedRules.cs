using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kartova.ArchitectureTests;

public class KeycloakRealmSeedRules
{
    private static readonly string SeedPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "deploy", "keycloak", "kartova-realm.json");

    [Fact]
    public void RealmSeed_RegistersKartovaWebPublicClientWithPkce()
    {
        File.Exists(SeedPath).Should().BeTrue($"realm seed not found at {SeedPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));
        var clients = doc.RootElement.GetProperty("clients");
        var web = clients.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("clientId").GetString() == "kartova-web");

        web.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "slice-4 spec §4.5 requires a kartova-web public client.");

        web.GetProperty("publicClient").GetBoolean().Should().BeTrue();
        web.GetProperty("standardFlowEnabled").GetBoolean().Should().BeTrue();
        web.GetProperty("directAccessGrantsEnabled").GetBoolean().Should().BeFalse(
            "password grant is forbidden — PKCE only.");

        var attrs = web.GetProperty("attributes");
        attrs.GetProperty("pkce.code.challenge.method").GetString()
            .Should().Be("S256");
        attrs.GetProperty("access.token.lifespan").GetString()
            .Should().Be("900",
                "SPA access tokens must remain short-lived (15 min) per slice-4 §4.5.");

        var redirects = web.GetProperty("redirectUris").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        redirects.Should().Contain(new[]
        {
            "http://localhost:5173/callback",
            "http://localhost:5173/silent-callback"
        });

        var origins = web.GetProperty("webOrigins").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        origins.Should().BeEquivalentTo(new[] { "http://localhost:5173" },
            "additional web origins would silently widen CORS for kartova-web tokens.");
    }

    [Fact]
    public void KartovaWebClient_ProjectsAudienceMapperToKartovaApi()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));
        var web = doc.RootElement.GetProperty("clients").EnumerateArray()
            .First(c => c.GetProperty("clientId").GetString() == "kartova-web");

        var mappers = web.GetProperty("protocolMappers").EnumerateArray().ToList();
        var audience = mappers.FirstOrDefault(m =>
            m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper");
        audience.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "kartova-web tokens must include kartova-api as audience so the API JWT validator accepts them.");
        audience.GetProperty("config")
            .GetProperty("included.client.audience").GetString()
            .Should().Be("kartova-api");
    }

    [Fact]
    public void KartovaWebClient_IncludesTenantIdProtocolMapper()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));
        var web = doc.RootElement.GetProperty("clients").EnumerateArray()
            .First(c => c.GetProperty("clientId").GetString() == "kartova-web");

        var mappers = web.GetProperty("protocolMappers").EnumerateArray().ToList();
        var tenantIdMapper = mappers.FirstOrDefault(m =>
            m.GetProperty("name").GetString() == "tenant_id" &&
            m.GetProperty("protocolMapper").GetString() == "oidc-usermodel-attribute-mapper");
        tenantIdMapper.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "kartova-web tokens must carry the tenant_id claim, same as kartova-api.");
        tenantIdMapper.GetProperty("config")
            .GetProperty("claim.name").GetString().Should().Be("tenant_id");
    }
}
