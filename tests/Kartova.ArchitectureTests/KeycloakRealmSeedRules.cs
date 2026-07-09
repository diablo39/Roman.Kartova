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
        // 4173 = the E2E web container origin (ADR-0113); the dev-server 5173 stays.
        CollectionAssert.Contains(redirects, "http://localhost:4173/callback");
        CollectionAssert.Contains(redirects, "http://localhost:4173/silent-callback");

        var origins = web.GetProperty("webOrigins").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        // Exact set (not Contains) so an UNINTENDED origin still fails this guard.
        // 5173 = vite dev server; 4173 = the E2E rootless web container (ADR-0113).
        CollectionAssert.AreEquivalent(
            new[] { "http://localhost:5173", "http://localhost:4173" },
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
    public void Realm_seed_includes_Viewer_role_excludes_TeamAdmin_role_and_includes_dev_users()
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
        Assert.IsFalse(roles.Contains("TeamAdmin"), "TeamAdmin realm role was removed in ADR-0101.");

        var usernames = doc.RootElement
            .GetProperty("users")
            .EnumerateArray()
            .Select(u => u.GetProperty("username").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(usernames.Contains("viewer@orga.kartova.local"),
            "Realm must include a 'viewer@orga' dev user.");
        Assert.IsTrue(usernames.Contains("team-admin@orga.kartova.local"),
            "Realm must include a 'team-admin@orga' dev user (now a realm Member, team Admin via membership).");
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

    [TestMethod]
    public void Every_role_in_KartovaRolePermissions_Map_has_at_least_one_dev_user()
    {
        Assert.IsTrue(File.Exists(SeedPath), $"realm seed not found at {SeedPath}");
        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));

        // Group dev users by the roles they hold.
        var usersByRole = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var user in doc.RootElement.GetProperty("users").EnumerateArray())
        {
            var username = user.GetProperty("username").GetString()!;
            if (!user.TryGetProperty("realmRoles", out var rolesProp)) continue;
            foreach (var roleElem in rolesProp.EnumerateArray())
            {
                var role = roleElem.GetString();
                if (string.IsNullOrEmpty(role)) continue;
                if (!usersByRole.TryGetValue(role, out var list))
                {
                    list = new List<string>();
                    usersByRole[role] = list;
                }
                list.Add(username);
            }
        }

        foreach (var role in KartovaRolePermissions.Map.Keys)
        {
            Assert.IsTrue(usersByRole.ContainsKey(role) && usersByRole[role].Count > 0,
                $"Role '{role}' in KartovaRolePermissions.Map must have at least one dev user in kartova-realm.json.");
        }
    }

    /// <summary>
    /// Slice-9 carry-forward #14: drift sentinel for the <c>kartova-admin</c> service-account
    /// client used by <c>KeycloakAdminClient</c> to provision invited users. If anyone disables
    /// service accounts, re-enables the password grant, or flips the client to public, the
    /// admin REST flow breaks silently — this test catches it at CI time.
    /// </summary>
    [TestMethod]
    public void RealmSeed_RegistersKartovaAdminClientWithServiceAccount()
    {
        Assert.IsTrue(File.Exists(SeedPath), $"realm seed not found at {SeedPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));
        var admin = doc.RootElement.GetProperty("clients").EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("clientId").GetString() == "kartova-admin");

        Assert.AreNotEqual(
            JsonValueKind.Undefined,
            admin.ValueKind,
            "slice-9 §6.7 requires a kartova-admin service-account client for invitation provisioning.");

        Assert.IsFalse(
            admin.GetProperty("publicClient").GetBoolean(),
            "kartova-admin must be a confidential client — it holds an admin secret.");
        Assert.IsTrue(
            admin.GetProperty("serviceAccountsEnabled").GetBoolean(),
            "kartova-admin must have serviceAccountsEnabled — client-credentials flow is the only allowed mechanism.");
        Assert.IsFalse(
            admin.GetProperty("directAccessGrantsEnabled").GetBoolean(),
            "kartova-admin must NOT have password grant enabled — confidential service-account only.");
        Assert.IsFalse(
            admin.GetProperty("standardFlowEnabled").GetBoolean(),
            "kartova-admin is not a user-facing OIDC client — standard flow must be disabled.");

        Assert.AreEqual(
            "admin-dev-secret",
            admin.GetProperty("secret").GetString(),
            "kartova-admin secret must match RealmSeedConstants.AdminClientSecret (test fixtures rely on it).");
    }

    /// <summary>
    /// Slice-9 carry-forward #14: the <c>kartova-admin</c> service-account user must hold the
    /// <c>realm-management</c> client roles required by <see cref="Kartova.SharedKernel.Identity.IKeycloakAdminClient"/>
    /// (create / search / delete users + assign realm roles). Without these the client gets
    /// 403 on every admin REST call and invitation acceptance silently fails.
    /// </summary>
    [TestMethod]
    public void KartovaAdmin_service_account_has_realm_management_admin_role()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));

        var serviceAccountUser = doc.RootElement.GetProperty("users").EnumerateArray()
            .FirstOrDefault(u => u.TryGetProperty("serviceAccountClientId", out var sac)
                                  && sac.GetString() == "kartova-admin");

        Assert.AreNotEqual(
            JsonValueKind.Undefined,
            serviceAccountUser.ValueKind,
            "kartova-admin must have a corresponding service-account user (Keycloak convention: " +
            "users[].serviceAccountClientId == 'kartova-admin').");

        Assert.IsTrue(
            serviceAccountUser.TryGetProperty("clientRoles", out var clientRoles),
            "kartova-admin service-account user must declare clientRoles for realm-management.");
        Assert.IsTrue(
            clientRoles.TryGetProperty("realm-management", out var realmMgmt),
            "kartova-admin service-account user must hold roles from the realm-management client.");

        var roles = realmMgmt.EnumerateArray()
            .Select(r => r.GetString())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.Ordinal);

        // KeycloakAdminClient calls CreateUser, SearchUsers, DeleteUser, AssignRealmRole.
        // manage-users covers create + delete + role assignment; view-users covers search.
        Assert.IsTrue(
            roles.Contains("manage-users"),
            "kartova-admin needs realm-management:manage-users (create/delete users, assign roles).");
        Assert.IsTrue(
            roles.Contains("view-users"),
            "kartova-admin needs realm-management:view-users (search users by email/query).");
    }
}
