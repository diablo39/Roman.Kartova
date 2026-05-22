using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Asserts the (role × catalog-endpoint) authorization matrix. Data-driven: each cell looks up
/// the required permission for the endpoint and checks the role's permission set in
/// <see cref="KartovaRolePermissions"/>.
/// </summary>
[TestClass]
public sealed class CatalogPermissionMatrixTests : CatalogIntegrationTestBase
{
    private const string OrgAdminEmail  = "admin@orga.kartova.local";
    private const string MemberEmail    = "member@orga.kartova.local";
    private const string TeamAdminEmail = "team-admin@orga.kartova.local";
    private const string ViewerEmail    = "viewer@orga.kartova.local";

    private static readonly (string Role, string Email)[] Roles =
    {
        (KartovaRoles.OrgAdmin,  OrgAdminEmail),
        (KartovaRoles.Member,    MemberEmail),
        (KartovaRoles.TeamAdmin, TeamAdminEmail),
        (KartovaRoles.Viewer,    ViewerEmail),
    };

    private static readonly (HttpMethod Method, string PathTemplate, string Permission)[] Endpoints =
    {
        (HttpMethod.Post, "/api/v1/catalog/applications",                   KartovaPermissions.CatalogApplicationsRegister),
        (HttpMethod.Get,  "/api/v1/catalog/applications",                   KartovaPermissions.CatalogRead),
        (HttpMethod.Get,  "/api/v1/catalog/applications/{id}",              KartovaPermissions.CatalogRead),
        (HttpMethod.Put,  "/api/v1/catalog/applications/{id}",              KartovaPermissions.CatalogApplicationsEditMetadata),
        (HttpMethod.Post, "/api/v1/catalog/applications/{id}/deprecate",    KartovaPermissions.CatalogApplicationsLifecycleForward),
        (HttpMethod.Post, "/api/v1/catalog/applications/{id}/decommission", KartovaPermissions.CatalogApplicationsLifecycleForward),
        (HttpMethod.Post, "/api/v1/catalog/applications/{id}/reactivate", KartovaPermissions.CatalogApplicationsLifecycleReverse),
        (HttpMethod.Post, "/api/v1/catalog/applications/{id}/un-decommission", KartovaPermissions.CatalogApplicationsLifecycleReverse),
    };

    [TestMethod]
    public async Task Every_role_endpoint_cell_matches_KartovaRolePermissions_Map()
    {
        // Seed a fixture Application as OrgAdmin so {id} substitution works on per-role calls.
        var seederClient = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registerResp = await seederClient.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new
            {
                name = "matrix-app-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                displayName = "Matrix App",
                description = "Seed for permission matrix test.",
            });
        Assert.IsTrue(registerResp.IsSuccessStatusCode,
            $"Seed registration must succeed (was {registerResp.StatusCode}).");
        var seeded = await registerResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        var appId = seeded!.Id;

        foreach (var (role, email) in Roles)
        {
            var client = await Fx.CreateAuthenticatedClientAsync(email, new[] { role });
            var grants = KartovaRolePermissions.ForRole(role);

            foreach (var (method, pathTemplate, perm) in Endpoints)
            {
                var url = pathTemplate.Replace("{id}", appId.ToString());
                using var req = new HttpRequestMessage(method, url);
                AttachShapeValidBody(req, method, pathTemplate);

                var resp = await client.SendAsync(req);
                var expectedForbidden = !grants.Contains(perm);

                if (expectedForbidden)
                {
                    Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode,
                        $"{role} calling {method} {pathTemplate} should be 403 (lacks {perm}). Actual: {resp.StatusCode}");
                }
                else
                {
                    Assert.AreNotEqual(HttpStatusCode.Forbidden, resp.StatusCode,
                        $"{role} calling {method} {pathTemplate} should NOT be 403 (has {perm}). Actual: {resp.StatusCode}");
                    Assert.AreNotEqual(HttpStatusCode.Unauthorized, resp.StatusCode,
                        $"{role} calling {method} {pathTemplate} should NOT be 401. Actual: {resp.StatusCode}");
                }
            }
        }
    }

    private static void AttachShapeValidBody(HttpRequestMessage req, HttpMethod method, string pathTemplate)
    {
        if (method == HttpMethod.Post && pathTemplate == "/api/v1/catalog/applications")
        {
            // Unique kebab name per request to avoid collisions across cells.
            req.Content = JsonContent.Create(new
            {
                name = "matrix-write-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                displayName = "Matrix Write",
                description = "Matrix shape body.",
            });
        }
        else if (method == HttpMethod.Put)
        {
            req.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue("\"AAAAAA==\""));
            req.Content = JsonContent.Create(new
            {
                displayName = "Matrix Edit",
                description = "Matrix edit body.",
            });
        }
        else if (pathTemplate.EndsWith("/deprecate"))
        {
            req.Content = JsonContent.Create(new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });
        }
        else if (pathTemplate.EndsWith("/un-decommission"))
        {
            req.Content = JsonContent.Create(new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });
        }
        // GET methods and /decommission have no body.
    }
}
