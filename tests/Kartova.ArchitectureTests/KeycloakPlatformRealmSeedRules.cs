using System.IO;
using System.Linq;
using System.Text.Json;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.ArchitectureTests;

/// <summary>
/// ADR-0118: drift sentinels for the platform-operator realm. The isolation guarantee rests on
/// this file: operators live here, tenants never do, and nothing in this realm lets a tenant
/// service account manage operator identities.
/// </summary>
[TestClass]
public sealed class KeycloakPlatformRealmSeedRules
{
    private static readonly string SeedPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "deploy", "keycloak", "kartova-platform-realm.json");

    private static JsonDocument Load()
    {
        Assert.IsTrue(File.Exists(SeedPath), $"platform realm seed not found at {SeedPath}");
        return JsonDocument.Parse(File.ReadAllText(SeedPath));
    }

    private static JsonElement Client(JsonDocument doc, string clientId) =>
        doc.RootElement.GetProperty("clients").EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("clientId").GetString() == clientId);

    [TestMethod]
    public void Platform_realm_is_named_kartova_platform_and_defines_only_the_platform_admin_role()
    {
        using var doc = Load();
        Assert.AreEqual("kartova-platform", doc.RootElement.GetProperty("realm").GetString());

        var roles = doc.RootElement.GetProperty("roles").GetProperty("realm").EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .ToArray();
        CollectionAssert.AreEquivalent(new[] { KartovaRoles.PlatformAdmin }, roles,
            "tenant roles (Viewer/Member/OrgAdmin) must never exist in the operator realm — ADR-0118.");
    }

    [TestMethod]
    public void Platform_realm_has_no_service_account_client_or_user()
    {
        using var doc = Load();
        var serviceAccountClients = doc.RootElement.GetProperty("clients").EnumerateArray()
            .Where(c => c.TryGetProperty("serviceAccountsEnabled", out var s) && s.GetBoolean())
            .Select(c => c.GetProperty("clientId").GetString())
            .ToArray();
        Assert.AreEqual(0, serviceAccountClients.Length,
            "no service account may manage operator identities — ADR-0118. Found: " + string.Join(", ", serviceAccountClients));

        var serviceAccountUsers = doc.RootElement.GetProperty("users").EnumerateArray()
            .Where(u => u.TryGetProperty("serviceAccountClientId", out _))
            .ToArray();
        Assert.AreEqual(0, serviceAccountUsers.Length);
    }

    [TestMethod]
    public void Admin_web_client_is_public_pkce_without_password_grant_and_targets_admin_api_audience()
    {
        using var doc = Load();
        var web = Client(doc, "kartova-admin-web");
        Assert.AreNotEqual(JsonValueKind.Undefined, web.ValueKind, "kartova-admin-web client missing.");
        Assert.IsTrue(web.GetProperty("publicClient").GetBoolean());
        Assert.IsTrue(web.GetProperty("standardFlowEnabled").GetBoolean());
        Assert.IsFalse(web.GetProperty("directAccessGrantsEnabled").GetBoolean(), "password grant is forbidden — PKCE only.");
        Assert.AreEqual("S256", web.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString());

        var audience = web.GetProperty("protocolMappers").EnumerateArray()
            .First(m => m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper");
        Assert.AreEqual("kartova-admin-api",
            audience.GetProperty("config").GetProperty("included.client.audience").GetString());
    }

    [TestMethod]
    public void Admin_api_audience_client_exists_and_is_bearer_only()
    {
        using var doc = Load();
        var api = Client(doc, "kartova-admin-api");
        Assert.AreNotEqual(JsonValueKind.Undefined, api.ValueKind,
            "the audience mapper's included.client.audience must reference an existing client.");
        Assert.IsTrue(api.GetProperty("bearerOnly").GetBoolean());
        Assert.IsFalse(api.GetProperty("standardFlowEnabled").GetBoolean());
        Assert.IsFalse(api.GetProperty("directAccessGrantsEnabled").GetBoolean());
    }

    [TestMethod]
    public void Platform_realm_rotates_refresh_tokens_and_revokes_on_reuse()
    {
        using var doc = Load();
        Assert.IsTrue(doc.RootElement.GetProperty("revokeRefreshToken").GetBoolean());
        Assert.AreEqual(0, doc.RootElement.GetProperty("refreshTokenMaxReuse").GetInt32());
    }

    [TestMethod]
    public void Platform_realm_hardening_parity_with_tenant_realm()
    {
        using var doc = Load();
        Assert.IsTrue(doc.RootElement.GetProperty("accessTokenLifespan").GetInt32() <= 300,
            "operator access tokens must be at least as short-lived as the tenant realm's — ADR-0118.");
        Assert.IsTrue(doc.RootElement.GetProperty("bruteForceProtected").GetBoolean(),
            "brute-force detection must be enabled on the operator realm — ADR-0118.");
        Assert.IsTrue(
            doc.RootElement.GetProperty("passwordPolicy").GetString()?.Contains("length(12)") == true,
            "operator password policy must enforce a minimum length of 12 — ADR-0118.");
    }

    [TestMethod]
    public void Only_kartova_admin_test_enables_direct_access_grants()
    {
        using var doc = Load();
        var passwordGrantClients = doc.RootElement.GetProperty("clients").EnumerateArray()
            .Where(c => c.TryGetProperty("directAccessGrantsEnabled", out var d) && d.GetBoolean())
            .Select(c => c.GetProperty("clientId").GetString())
            .ToArray();
        CollectionAssert.AreEquivalent(new[] { "kartova-admin-test" }, passwordGrantClients,
            "only the dev/test client may enable the password grant — ADR-0118. A missing " +
            "directAccessGrantsEnabled property is treated as false, matching KeyCloak's default.");
    }

    [TestMethod]
    public void Platform_realm_has_a_dev_user_holding_platform_admin()
    {
        using var doc = Load();
        var holders = doc.RootElement.GetProperty("users").EnumerateArray()
            .Where(u => u.TryGetProperty("realmRoles", out var r)
                        && r.EnumerateArray().Any(x => x.GetString() == KartovaRoles.PlatformAdmin))
            .Select(u => u.GetProperty("username").GetString())
            .ToArray();
        CollectionAssert.Contains(holders, "platform-admin@kartova.local");
    }

    [TestMethod]
    public void Admin_web_client_redirects_are_exactly_the_dev_and_container_origins()
    {
        using var doc = Load();
        var web = Client(doc, "kartova-admin-web");

        var redirects = web.GetProperty("redirectUris").EnumerateArray().Select(x => x.GetString()).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "http://localhost:5174/callback", "http://localhost:5174/silent-callback", "http://localhost:4174/callback" },
            redirects,
            "no wildcard redirects — exact callback URIs only (ADR-0118).");

        var origins = web.GetProperty("webOrigins").EnumerateArray().Select(x => x.GetString()).ToArray();
        CollectionAssert.AreEquivalent(new[] { "http://localhost:5174", "http://localhost:4174" }, origins);
    }

    [TestMethod]
    public void Platform_realm_has_a_dev_user_without_any_realm_role()
    {
        using var doc = Load();
        var user = doc.RootElement.GetProperty("users").EnumerateArray()
            .FirstOrDefault(u => u.GetProperty("username").GetString() == "operator-norole@kartova.local");
        Assert.AreNotEqual(JsonValueKind.Undefined, user.ValueKind,
            "operator-norole@kartova.local drives the no-access E2E + gate-9 path (S2).");

        var roles = user.TryGetProperty("realmRoles", out var r)
            ? r.EnumerateArray().Select(x => x.GetString()).ToArray()
            : Array.Empty<string?>();
        Assert.AreEqual(0, roles.Length, "the no-role dev user must hold no realm roles: " + string.Join(", ", roles));
    }
}
