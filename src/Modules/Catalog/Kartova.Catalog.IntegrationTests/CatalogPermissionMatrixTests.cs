using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Asserts the (role × catalog-endpoint) authorization matrix. Data-driven: each cell looks up
/// the required permission for the endpoint and checks the role's permission set in
/// <see cref="KartovaRolePermissions"/>.
///
/// Slice 8 — non-OrgAdmin callers must also be members of the target application's team for
/// mutation endpoints to clear the resource-auth gate (<see cref="KartovaTeamPolicies.ApplicationTeamScoped"/>).
/// The arrange phase seeds a team in the same tenant, reassigns the seed app to it, and adds
/// the Member/TeamAdmin test users as team members so the claim-level matrix isn't masked by
/// resource-level denial. A separate <see cref="Team_scope_matrix_for_metadata_edit"/> test
/// covers the orthogonal team-scope dimension (in-team / out-of-team / unassigned).
/// </summary>
[TestClass]
public sealed class CatalogPermissionMatrixTests : CatalogIntegrationTestBase
{
    private const byte TeamRoleMember = 1;
    private const byte TeamRoleAdmin  = 2;

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
                displayName = "Matrix App",
                description = "Seed for permission matrix test.",
            });
        Assert.IsTrue(registerResp.IsSuccessStatusCode,
            $"Seed registration must succeed (was {registerResp.StatusCode}).");
        var seeded = await registerResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        var appId = seeded!.Id;

        // Slice 8: bind the seed app to a team in the same tenant and add Member + TeamAdmin
        // as members of that team. Without these rows the resource gate denies the mutation
        // cells (Decision #9 — null team_id blocks non-OrgAdmin), which would mask the
        // claim-level assertions the matrix is designed to verify.
        var tenant = KartovaApiFixtureBase.TenantFor(OrgAdminEmail);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "Matrix Team");
        await Fx.SetApplicationTeamAsync(appId, teamId);
        var memberSub    = await Fx.GetSubClaimAsync(MemberEmail);
        var teamAdminSub = await Fx.GetSubClaimAsync(TeamAdminEmail);
        await Fx.SeedTeamMembershipAsync(teamId, memberSub,    TeamRoleMember);
        await Fx.SeedTeamMembershipAsync(teamId, teamAdminSub, TeamRoleAdmin);

        try
        {
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
                        // Allowed means != 403 and != 401. The specific 2xx/4xx status (e.g. 409 for
                        // lifecycle state mismatch on the seeded Active application) is irrelevant
                        // here — per-endpoint integration tests cover response-shape correctness.
                        Assert.AreNotEqual(HttpStatusCode.Forbidden, resp.StatusCode,
                            $"{role} calling {method} {pathTemplate} should NOT be 403 (has {perm}). Actual: {resp.StatusCode}");
                        Assert.AreNotEqual(HttpStatusCode.Unauthorized, resp.StatusCode,
                            $"{role} calling {method} {pathTemplate} should NOT be 401. Actual: {resp.StatusCode}");
                    }
                }
            }
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(tenant.Value);
        }
    }

    /// <summary>
    /// Spec §10 / ADR-0098 — orthogonal to the role × permission matrix above:
    /// asserts the team-scope dimension of <see cref="KartovaTeamPolicies.ApplicationTeamScoped"/>
    /// on a single representative mutation endpoint (<c>PUT /applications/{id}</c>).
    ///
    /// Cells covered:
    /// <list type="bullet">
    ///   <item>Member in team-A vs app in team-A → not 403 (gate succeeds).</item>
    ///   <item>Member in team-A vs app in team-B → 403 (different team).</item>
    ///   <item>Member in team-A vs unassigned app → 403 (Decision #9 — null team_id blocks non-OrgAdmin).</item>
    ///   <item>OrgAdmin vs unassigned app → not 403 (OrgAdmin shortcut bypasses team check).</item>
    /// </list>
    /// </summary>
    [TestMethod]
    public async Task Team_scope_matrix_for_metadata_edit()
    {
        var tenant = KartovaApiFixtureBase.TenantFor(MemberEmail);

        var teamA = await Fx.SeedTeamInOrganizationAsync(tenant, "Team A");
        var teamB = await Fx.SeedTeamInOrganizationAsync(tenant, "Team B");

        var appInTeamA   = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamA,
            namePrefix: "matrix-scope-a");
        var appInTeamB   = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamB,
            namePrefix: "matrix-scope-b");
        var appUnassigned = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId: null,
            namePrefix: "matrix-scope-u");

        var memberSub = await Fx.GetSubClaimAsync(MemberEmail);
        await Fx.SeedTeamMembershipAsync(teamA, memberSub, TeamRoleMember);

        try
        {
            var memberClient = await Fx.CreateAuthenticatedClientAsync(MemberEmail, new[] { KartovaRoles.Member });
            var adminClient  = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });

            // Member in team-A vs app in team-A → gate passes (not 403). The mutation will
            // fail downstream with 412 Precondition Failed (matrix-style placeholder ETag),
            // which is the desired evidence that the auth gate did not short-circuit.
            var memberInTeamResp = await SendEditMetadata(memberClient, appInTeamA);
            Assert.AreNotEqual(HttpStatusCode.Forbidden, memberInTeamResp.StatusCode,
                $"Member in team-A on app in team-A should not be 403. Actual: {memberInTeamResp.StatusCode}");

            // Member in team-A vs app in team-B → 403 (different team).
            var memberWrongTeamResp = await SendEditMetadata(memberClient, appInTeamB);
            Assert.AreEqual(HttpStatusCode.Forbidden, memberWrongTeamResp.StatusCode,
                $"Member in team-A on app in team-B should be 403. Actual: {memberWrongTeamResp.StatusCode}");

            // Member in team-A vs unassigned app → 403 (Decision #9).
            var memberUnassignedResp = await SendEditMetadata(memberClient, appUnassigned);
            Assert.AreEqual(HttpStatusCode.Forbidden, memberUnassignedResp.StatusCode,
                $"Member on unassigned app should be 403. Actual: {memberUnassignedResp.StatusCode}");

            // OrgAdmin vs unassigned app → not 403 (OrgAdmin shortcut bypasses team check).
            var orgAdminUnassignedResp = await SendEditMetadata(adminClient, appUnassigned);
            Assert.AreNotEqual(HttpStatusCode.Forbidden, orgAdminUnassignedResp.StatusCode,
                $"OrgAdmin on unassigned app should not be 403. Actual: {orgAdminUnassignedResp.StatusCode}");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, "matrix-scope-a");
            await Fx.DeleteApplicationsByPrefixAsync(tenant, "matrix-scope-b");
            await Fx.DeleteApplicationsByPrefixAsync(tenant, "matrix-scope-u");
            await Fx.DeleteTeamsForTenantAsync(tenant.Value);
        }
    }

    private static async Task<HttpResponseMessage> SendEditMetadata(HttpClient client, Guid appId)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/applications/{appId}")
        {
            Content = JsonContent.Create(new
            {
                displayName = "Matrix Scope Edit",
                description = "Team-scope matrix probe.",
            }),
        };
        // Matrix-style placeholder ETag — the IfMatchEndpointFilter parses it successfully
        // (well-formed base64), then the auth gate runs in the delegate. On gate failure we
        // observe 403; on gate success the request continues and fails downstream on the
        // precondition mismatch — never 403 from the gate.
        req.Headers.IfMatch.Add(new EntityTagHeaderValue("\"AAAAAA==\""));
        return await client.SendAsync(req);
    }

    private static void AttachShapeValidBody(HttpRequestMessage req, HttpMethod method, string pathTemplate)
    {
        if (method == HttpMethod.Post && pathTemplate == "/api/v1/catalog/applications")
        {
            // ADR-0098: the kebab `name` column was retired — register bodies carry
            // only displayName + description. Per-cell uniqueness is no longer needed
            // because nothing on the register path enforces global uniqueness on
            // displayName for the matrix's purposes.
            req.Content = JsonContent.Create(new
            {
                displayName = "Matrix Write",
                description = "Matrix shape body.",
            });
        }
        else if (method == HttpMethod.Put)
        {
            req.Headers.IfMatch.Add(new EntityTagHeaderValue("\"AAAAAA==\""));
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
