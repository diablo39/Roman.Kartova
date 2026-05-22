using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.ArchitectureTests;

[TestClass]
public sealed class KeycloakRealmSeedRules
{
    private static readonly string SeedPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "deploy", "keycloak", "kartova-realm.json");

    [TestMethod]
    public void RealmSeed_RegistersKartovaWebPublicClientWithPkce()
    {
        Assert.IsTrue(File.Exists(SeedPath), $"realm seed not found at {SeedPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));
        var clients = doc.RootElement.GetProperty("clients");
        var web = clients.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("clientId").GetString() == "kartova-web");

        Assert.AreNotEqual(
            JsonValueKind.Undefined,
            web.ValueKind,
            "slice-4 spec §4.5 requires a kartova-web public client.");

        Assert.IsTrue(web.GetProperty("publicClient").GetBoolean());
        Assert.IsTrue(web.GetProperty("standardFlowEnabled").GetBoolean());
        Assert.IsFalse(
            web.GetProperty("directAccessGrantsEnabled").GetBoolean(),
            "password grant is forbidden — PKCE only.");

        var attrs = web.GetProperty("attributes");
        Assert.AreEqual(
            "S256",
            attrs.GetProperty("pkce.code.challenge.method").GetString());
        Assert.AreEqual(
            "900",
            attrs.GetProperty("access.token.lifespan").GetString(),
            "SPA access tokens must remain short-lived (15 min) per slice-4 §4.5.");

        var redirects = web.GetProperty("redirectUris").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        CollectionAssert.Contains(redirects, "http://localhost:5173/callback");
        CollectionAssert.Contains(redirects, "http://localhost:5173/silent-callback");

        var origins = web.GetProperty("webOrigins").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        CollectionAssert.AreEquivalent(
            new[] { "http://localhost:5173" },
            origins,
            "additional web origins would silently widen CORS for kartova-web tokens.");
    }

    [TestMethod]
    public void KartovaWebClient_ProjectsAudienceMapperToKartovaApi()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));
        var web = doc.RootElement.GetProperty("clients").EnumerateArray()
            .First(c => c.GetProperty("clientId").GetString() == "kartova-web");

        var mappers = web.GetProperty("protocolMappers").EnumerateArray().ToList();
        var audience = mappers.FirstOrDefault(m =>
            m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper");
        Assert.AreNotEqual(
            JsonValueKind.Undefined,
            audience.ValueKind,
            "kartova-web tokens must include kartova-api as audience so the API JWT validator accepts them.");
        Assert.AreEqual(
            "kartova-api",
            audience.GetProperty("config").GetProperty("included.client.audience").GetString());
    }

    [TestMethod]
    public void KartovaWebClient_IncludesTenantIdProtocolMapper()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));
        var web = doc.RootElement.GetProperty("clients").EnumerateArray()
            .First(c => c.GetProperty("clientId").GetString() == "kartova-web");

        var mappers = web.GetProperty("protocolMappers").EnumerateArray().ToList();
        var tenantIdMapper = mappers.FirstOrDefault(m =>
            m.GetProperty("name").GetString() == "tenant_id" &&
            m.GetProperty("protocolMapper").GetString() == "oidc-usermodel-attribute-mapper");
        Assert.AreNotEqual(
            JsonValueKind.Undefined,
            tenantIdMapper.ValueKind,
            "kartova-web tokens must carry the tenant_id claim, same as kartova-api.");
        Assert.AreEqual(
            "tenant_id",
            tenantIdMapper.GetProperty("config").GetProperty("claim.name").GetString());
    }

    [TestMethod]
    public void Realm_seed_includes_Viewer_and_TeamAdmin_roles_and_dev_users()
    {
        Assert.IsTrue(File.Exists(SeedPath), $"realm seed not found at {SeedPath}");
        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));

        var roles = doc.RootElement
            .GetProperty("roles")
            .GetProperty("realm")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(roles.Contains("Viewer"), "Realm must include 'Viewer' role.");
        Assert.IsTrue(roles.Contains("TeamAdmin"), "Realm must include 'TeamAdmin' role.");

        var usernames = doc.RootElement
            .GetProperty("users")
            .EnumerateArray()
            .Select(u => u.GetProperty("username").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(usernames.Contains("viewer@orga.kartova.local"),
            "Realm must include a 'viewer@orga' dev user.");
        Assert.IsTrue(usernames.Contains("team-admin@orga.kartova.local"),
            "Realm must include a 'team-admin@orga' dev user.");
    }

    [TestMethod]
    public void Every_KartovaRoles_constant_except_ServiceAccount_appears_in_realm_seed()
    {
        Assert.IsTrue(File.Exists(SeedPath), $"realm seed not found at {SeedPath}");
        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));

        var realmRoles = doc.RootElement.GetProperty("roles").GetProperty("realm")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var constantValues = typeof(KartovaRoles)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Value: (string)f.GetRawConstantValue()!))
            .Where(t => t.Name != nameof(KartovaRoles.ServiceAccount))
            .ToArray();

        foreach (var (name, value) in constantValues)
        {
            Assert.IsTrue(realmRoles.Contains(value),
                $"KartovaRoles.{name} = '{value}' has no matching entry in kartova-realm.json roles.realm.");
        }
    }
}
