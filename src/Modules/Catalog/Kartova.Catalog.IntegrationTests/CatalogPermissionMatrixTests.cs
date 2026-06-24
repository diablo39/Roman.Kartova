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
/// the Member test user as a team member so the claim-level matrix isn't masked by
/// resource-level denial. A separate <see cref="Team_scope_matrix_for_metadata_edit"/> test
/// covers the orthogonal team-scope dimension (in-team / out-of-team / unassigned).
/// </summary>
[TestClass]
public sealed class CatalogPermissionMatrixTests : CatalogIntegrationTestBase
{
    private const byte TeamRoleMember = 1;

    private const string OrgAdminEmail  = "admin@orga.kartova.local";
    private const string MemberEmail    = "member@orga.kartova.local";
    private const string ViewerEmail    = "viewer@orga.kartova.local";

    private static readonly (string Role, string Email)[] Roles =
    {
        (KartovaRoles.OrgAdmin,  OrgAdminEmail),
        (KartovaRoles.Member,    MemberEmail),
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
        (HttpMethod.Post, "/api/v1/catalog/services",                       KartovaPermissions.CatalogServicesRegister),
        (HttpMethod.Get,  "/api/v1/catalog/services",                       KartovaPermissions.CatalogRead),
        (HttpMethod.Get,  "/api/v1/catalog/services/{svcId}",               KartovaPermissions.CatalogRead),
        (HttpMethod.Post,   "/api/v1/catalog/relationships",                  KartovaPermissions.CatalogRelationshipsWrite),
        (HttpMethod.Delete, "/api/v1/catalog/relationships/{relId}",         KartovaPermissions.CatalogRelationshipsWrite),
        (HttpMethod.Get,  "/api/v1/catalog/relationships",                  KartovaPermissions.CatalogRead),
    };

    [TestMethod]
    public async Task Every_role_endpoint_cell_matches_KartovaRolePermissions_Map()
    {
        // Slice 8 / ADR-0103: seed a team in the same tenant FIRST (register now requires
        // a valid owning team), then register the seed app owned by it and add the Member
        // user as a member of that team. Without the membership the resource gate denies
        // the mutation cells, which would mask the claim-level assertions the matrix is
        // designed to verify.
        var tenant = KartovaApiFixtureBase.TenantFor(OrgAdminEmail);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "Matrix Team");

        // Seed a fixture Application as OrgAdmin so {id} substitution works on per-role calls.
        var seederClient = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registerResp = await seederClient.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new
            {
                displayName = "Matrix App",
                description = "Seed for permission matrix test.",
                teamId,
            });
        Assert.IsTrue(registerResp.IsSuccessStatusCode,
            $"Seed registration must succeed (was {registerResp.StatusCode}).");
        var seeded = await registerResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        var appId = seeded!.Id;

        // Seed a fixture Service as OrgAdmin so {svcId} substitution works on per-role calls.
        var registerSvcResp = await seederClient.PostAsJsonAsync(
            "/api/v1/catalog/services",
            new
            {
                displayName = "Matrix Svc",
                description = "Seed service for permission matrix test.",
                teamId,
                endpoints = Array.Empty<object>(),
            });
        Assert.IsTrue(registerSvcResp.IsSuccessStatusCode,
            $"Seed service registration must succeed (was {registerSvcResp.StatusCode}).");
        var seededSvc = await registerSvcResp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        var svcId = seededSvc!.Id;

        // Seed a fixture Relationship as OrgAdmin so {relId} substitution works on DELETE calls.
        // We seed a second service to be the target, then create the relationship.
        var registerSvc2Resp = await seederClient.PostAsJsonAsync(
            "/api/v1/catalog/services",
            new
            {
                displayName = "Matrix Svc2",
                description = "Seed service 2 for permission matrix test.",
                teamId,
                endpoints = Array.Empty<object>(),
            });
        Assert.IsTrue(registerSvc2Resp.IsSuccessStatusCode,
            $"Seed service 2 registration must succeed (was {registerSvc2Resp.StatusCode}).");
        var seededSvc2 = await registerSvc2Resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        var svc2Id = seededSvc2!.Id;

        var seedRelResp = await seederClient.PostAsJsonAsync(
            "/api/v1/catalog/relationships",
            new
            {
                sourceKind = "Service",
                sourceId   = svcId,
                type       = "DependsOn",
                targetKind = "Service",
                targetId   = svc2Id,
            });
        Assert.IsTrue(seedRelResp.IsSuccessStatusCode,
            $"Seed relationship must succeed (was {seedRelResp.StatusCode}).");
        var seededRel = await seedRelResp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        var relId = seededRel!.Id;

        var memberSub = await Fx.GetSubClaimAsync(MemberEmail);
        await Fx.SeedTeamMembershipAsync(teamId, memberSub, TeamRoleMember);

        try
        {
            foreach (var (role, email) in Roles)
            {
                var client = await Fx.CreateAuthenticatedClientAsync(email, new[] { role });
                var grants = KartovaRolePermissions.ForRole(role);

                foreach (var (method, pathTemplate, perm) in Endpoints)
                {
                    var url = pathTemplate
                        .Replace("{id}", appId.ToString())
                        .Replace("{svcId}", svcId.ToString())
                        .Replace("{relId}", relId.ToString());
                    using var req = new HttpRequestMessage(method, url);
                    AttachShapeValidBody(req, method, pathTemplate, teamId);

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
    ///   <item>Member in team-A vs app in an unrelated team → 403 (non-OrgAdmin not a member).</item>
    ///   <item>OrgAdmin vs app in an unrelated team → not 403 (OrgAdmin shortcut bypasses team check).</item>
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
        // ADR-0103: no unassigned apps. Seed an app owned by an unrelated (random) team
        // the Member is not in — the gate must still 403 for the Member and pass for OrgAdmin.
        var appUnrelatedTeam = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId: null,
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

            // Member in team-A vs app in an unrelated team → 403 (non-OrgAdmin not a member).
            var memberUnrelatedResp = await SendEditMetadata(memberClient, appUnrelatedTeam);
            Assert.AreEqual(HttpStatusCode.Forbidden, memberUnrelatedResp.StatusCode,
                $"Member on app in an unrelated team should be 403. Actual: {memberUnrelatedResp.StatusCode}");

            // OrgAdmin vs app in an unrelated team → not 403 (OrgAdmin shortcut bypasses team check).
            var orgAdminUnrelatedResp = await SendEditMetadata(adminClient, appUnrelatedTeam);
            Assert.AreNotEqual(HttpStatusCode.Forbidden, orgAdminUnrelatedResp.StatusCode,
                $"OrgAdmin on app in an unrelated team should not be 403. Actual: {orgAdminUnrelatedResp.StatusCode}");
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

    private static void AttachShapeValidBody(HttpRequestMessage req, HttpMethod method, string pathTemplate, Guid teamId)
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
                teamId,
            });
        }
        else if (method == HttpMethod.Post && pathTemplate == "/api/v1/catalog/services")
        {
            req.Content = JsonContent.Create(new
            {
                displayName = "Matrix Svc",
                description = "Matrix shape body.",
                teamId,
                endpoints = Array.Empty<object>(),
            });
        }
        else if (method == HttpMethod.Post && pathTemplate == "/api/v1/catalog/relationships")
        {
            // Shape-valid body: well-formed types. The claim gate fires before entity
            // lookup so the Guid values need not resolve — only permission matters here.
            req.Content = JsonContent.Create(new
            {
                sourceKind = "Service",
                sourceId   = Guid.NewGuid(),
                type       = "DependsOn",
                targetKind = "Service",
                targetId   = Guid.NewGuid(),
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
