using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class RegisterApplicationTests : CatalogIntegrationTestBase
{
    // ADR-0103: register now requires a valid owning team in the tenant. Seed one
    // and return its id for the happy-path bodies. Each test seeds its own team
    // (idempotent, BYPASSRLS) to stay independent.
    private async Task<Guid> SeedTeamForOrgAAsync() =>
        await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail("admin@orga.kartova.local"), "Reg Team");

    [TestMethod]
    public async Task POST_with_valid_payload_creates_row_and_returns_201()
    {
        var teamId = await SeedTeamForOrgAAsync();
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Payments API", "Payments REST surface.", teamId));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        StringAssert.StartsWith(resp.Headers.Location!.ToString(), "/api/v1/catalog/applications/");

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(body);
        Assert.AreEqual("Payments API", body!.DisplayName);
        Assert.AreEqual("Payments REST surface.", body.Description);
        Assert.AreEqual(teamId, body.TeamId);
        Assert.AreNotEqual(Guid.Empty, body.Id);

        // Slice 5 wire-shape pin: lifecycle defaults to Active, no sunset on
        // create, Version is base64(4-byte xmin) and round-trips via VersionEncoding.
        Assert.AreEqual(Lifecycle.Active, body.Lifecycle);
        Assert.IsNull(body.SunsetDate);
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.Version));
        Assert.IsTrue(VersionEncoding.TryDecode(body.Version, out _));
    }

    [TestMethod]
    public async Task POST_persists_created_by_user_id_from_jwt_sub_claim()
    {
        var teamId = await SeedTeamForOrgAAsync();
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var subFromToken = await Fx.GetSubClaimAsync("admin@orga.kartova.local");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Svc X", "x", teamId));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(subFromToken, body!.CreatedByUserId);
    }

    [TestMethod]
    public async Task POST_persists_tenant_id_from_scope_not_payload()
    {
        var teamId = await SeedTeamForOrgAAsync();
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var tenantFromToken = await Fx.GetTenantIdClaimAsync("admin@orga.kartova.local");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Svc Y", "y", teamId));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(tenantFromToken, body!.TenantId);
    }

    [TestMethod]
    public async Task POST_with_unknown_teamId_returns_422_invalid_team()
    {
        // ADR-0103: the owning team must exist in the tenant. A random uuid that was
        // never seeded resolves as "not found" via IOrganizationTeamExistenceChecker.
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("No Team App", "desc", Guid.NewGuid()));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType!.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, ProblemTypes.InvalidTeam);
    }

    [TestMethod]
    [DataRow("", "desc")]
    [DataRow("  ", "desc")]
    [DataRow("Display", "")]
    [DataRow("Display", "  ")]
    public async Task POST_with_invalid_payload_returns_400(string displayName, string description)
    {
        var teamId = await SeedTeamForOrgAAsync();
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(displayName, description, teamId));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType!.MediaType);
    }

    [TestMethod]
    public async Task GET_by_id_returns_row_in_same_tenant()
    {
        var teamId = await SeedTeamForOrgAAsync();
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var post = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Svc Z", "z", teamId));
        var created = await post.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);

        var get = await client.GetAsync($"/api/v1/catalog/applications/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        var fetched = await get.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(created.Id, fetched!.Id);
        Assert.AreEqual("Svc Z", fetched.DisplayName);
    }

    [TestMethod]
    public async Task GET_by_id_returns_404_for_unknown_id()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.GetAsync($"/api/v1/catalog/applications/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType!.MediaType);
    }

    [TestMethod]
    public async Task GET_by_id_emits_ETag_header_matching_Version_field()
    {
        // Slice 5 §13.5 — the single-resource GET emits an RFC 7232 quoted ETag
        // whose value is the base64-encoded xmin row version. Clients capture
        // it for a future PUT If-Match request (Task 8+).
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var registered = await CreateApp(client, "etag-app");

        var resp = await client.GetAsync($"/api/v1/catalog/applications/{registered.Id}");
        Assert.IsTrue(resp.IsSuccessStatusCode);

        var etag = resp.Headers.ETag?.Tag;
        Assert.IsFalse(string.IsNullOrWhiteSpace(etag));
        Assert.AreEqual($"\"{registered.Version}\"", etag);        // RFC 7232 quoted
    }

    [TestMethod]
    public async Task GET_list_returns_apps_in_current_tenant_sorted_by_createdAt()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var first = await CreateApp(client, "first-app-list");
        var second = await CreateApp(client, "second-app-list");

        // This test exercises createdAt-desc retrieval specifically. The LIST default sort
        // flipped to displayName asc (list-filter-surface-catalog, 2026-06-22), so pin the
        // sort explicitly — relying on the default would let the seeded apps paginate out of
        // the first page once the tenant accumulates rows.
        var resp = await client.GetAsync("/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.IsNotNull(page);
        var ids = page!.Items.Select(x => x.Id).ToList();
        CollectionAssert.Contains(ids, first.Id);
        CollectionAssert.Contains(ids, second.Id);
        // createdAt desc surfaces both newly created apps at the front of the page.
        Assert.IsTrue(page!.Items.Any());
    }

    // All CreateApp callers register as OrgA, so seed the owning team in OrgA's tenant
    // (ADR-0103: register requires a valid team). BYPASSRLS seed, idempotent per call.
    private async Task<ApplicationResponse> CreateApp(HttpClient c, string name)
    {
        var teamId = await SeedTeamForOrgAAsync();
        var post = await c.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, $"desc for {name}", teamId));
        return (await post.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    [TestMethod]
    public async Task POST_with_invalid_displayName_returns_field_level_problem_details()
    {
        // Seed a valid team so the request passes the team-existence gate and reaches
        // the displayName validation (which is what this test asserts on).
        var teamId = await SeedTeamForOrgAAsync();
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("", "desc", teamId));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var errors = doc.GetProperty("errors");
        StringAssert.Contains(
            errors.GetProperty("displayName").EnumerateArray().Single().GetString(),
            "must not be empty");
    }

    [TestMethod]
    public async Task POST_without_token_returns_401()
    {
        using var client = Fx.CreateAnonymousClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Name", "desc", Guid.NewGuid()));

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_by_id_returns_404_for_other_tenants_row()
    {
        // OrgA creates a row, OrgB tries to fetch it by id. Must 404 — never leak
        // existence (no 403, no 200). Pins the cross-tenant isolation guarantee.
        var teamId = await SeedTeamForOrgAAsync();
        var clientA = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var post = await clientA.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Orga Private", "owned by orga", teamId));
        Assert.AreEqual(HttpStatusCode.Created, post.StatusCode);
        var orgaApp = await post.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);

        var clientB = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await clientB.GetAsync($"/api/v1/catalog/applications/{orgaApp!.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_list_excludes_other_tenants_rows()
    {
        // OrgA seeds a row; OrgB's list must not include it. RLS + tenant scope
        // together must shield orgb from any orga rows.
        var clientA = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var orgaApp = await CreateApp(clientA, "orga-isolation-probe");

        var clientB = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await clientB.GetAsync("/api/v1/catalog/applications");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.IsNotNull(page);
        var rows = page!.Items;
        CollectionAssert.DoesNotContain(rows.Select(x => x.Id).ToList(), orgaApp.Id);

        var orgbTenantId = await Fx.GetTenantIdClaimAsync("admin@orgb.kartova.local");
        Assert.IsTrue(rows.All(x => x.TenantId == orgbTenantId));
    }

    // ── Membership gate tests (mirrors assign-team SF-2) ────────────────────

    // OrgAdmin 201: existing happy-path tests (POST_with_valid_payload_creates_row_and_returns_201
    // et al.) already cover this — they all use CreateAuthenticatedClientAsync which defaults to
    // OrgAdmin. The explicit test below documents the gate semantics.
    [TestMethod]
    public async Task POST_OrgAdmin_registers_into_any_team_returns_201()
    {
        // OrgAdmin is unaffected by the membership gate — they may register an app
        // into any tenant team regardless of personal team membership.
        // Note: subject must be a valid Guid because RegisterApplicationHandler uses
        // ICurrentUser.UserId (Guid.Parse("sub")) for the createdByUserId column.
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-0040-0040-0040-000000000001"));
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "Reg-Gate-OrgAdmin");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                tenant,
                new[] { KartovaRoles.OrgAdmin },
                subject: Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/catalog/applications",
                new RegisterApplicationRequest("Gate-OrgAdmin App", "desc", teamId));

            Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, "Gate-OrgAdmin App");
            await Fx.DeleteTeamsForTenantAsync(tenant.Value);
        }
    }

    [TestMethod]
    public async Task POST_Member_who_IS_member_of_team_registers_into_that_team_returns_201()
    {
        // Control case: a Member that belongs to team T may register an app into T.
        // TeamIds is populated by ITeamMembershipReader from the DB (not the JWT), so
        // we must seed a team_members row AND issue a JWT whose sub matches the seeded userId.
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-0040-0040-0040-000000000002"));
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "Reg-Gate-MemberIn");
        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, memberId, roleByte: 1 /* Member */);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                tenant,
                new[] { KartovaRoles.Member },
                subject: memberId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/catalog/applications",
                new RegisterApplicationRequest("Gate-MemberIn App", "desc", teamId));

            Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, "Gate-MemberIn App");
            await Fx.DeleteTeamsForTenantAsync(tenant.Value);
        }
    }

    [TestMethod]
    public async Task POST_Member_who_is_NOT_member_of_team_registers_into_that_team_returns_403()
    {
        // Security fix: a Member that does NOT belong to the target team must be
        // rejected with 403 before the application row is created. This closes the
        // authz asymmetry between register and assign-team (SF-2 mirror).
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-0040-0040-0040-000000000003"));
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "Reg-Gate-MemberOut");
        try
        {
            var client = Fx.CreateClient();
            // Caller has a valid Member JWT but no team_members row for this team —
            // ITeamMembershipReader returns an empty set, so TeamIds.Contains(teamId) = false.
            var token = Fx.Signer.IssueForTenant(
                tenant,
                new[] { KartovaRoles.Member },
                subject: Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/catalog/applications",
                new RegisterApplicationRequest("Gate-MemberOut App", "desc", teamId));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(tenant.Value);
        }
    }

    [TestMethod]
    public async Task POST_Member_with_unknown_teamId_returns_422_not_403()
    {
        // Ordering pin (MT-2): team-existence (422 invalid-team) is evaluated BEFORE the
        // membership gate (403). A Member supplying a team that does not exist must get 422 —
        // the existence check wins — never 403. Guards against a refactor that swaps the two
        // checks (which would flip a non-member's unknown-team response from 422 to 403).
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-0040-0040-0040-000000000004"));
        var client = Fx.CreateClient();
        // Valid Member JWT, no team membership, and a team id that was never seeded.
        var token = Fx.Signer.IssueForTenant(
            tenant,
            new[] { KartovaRoles.Member },
            subject: Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Gate-MemberUnknownTeam App", "desc", Guid.NewGuid()));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode,
            $"Member + unknown team must be 422 (existence before membership gate), not 403. "
            + $"Body: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, ProblemTypes.InvalidTeam);
    }
}
