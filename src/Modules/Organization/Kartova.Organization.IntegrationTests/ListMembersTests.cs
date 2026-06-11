using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET /api/v1/organizations/users</c> — cursor-paginated
/// members directory (slice 10, spec §4). Verifies listing, role filtering, permission
/// guard (Viewer has <c>org.users.read</c>), and that the typeahead is still
/// reachable at its relocated path <c>/users/search</c>.
/// </summary>
[TestClass]
public sealed class ListMembersTests : OrganizationIntegrationTestBase
{
    private const byte TeamMemberRole = 1; // TeamRole.Member
    private const byte TeamAdminRole  = 2; // TeamRole.Admin

    // ---------- cleanup helper -----------------------------------------------

    /// <summary>
    /// Best-effort teardown: deletes user rows (by id), team_members rows + team
    /// rows (via <see cref="KartovaApiFixture.DeleteTeamsForTenantAsync"/>), then
    /// the organizations row. Each step is wrapped so one failure doesn't skip
    /// the others — mirrors the convention in <see cref="UserSearchTests"/>.
    /// </summary>
#pragma warning disable CA1031 // best-effort test teardown
    private static async Task CleanupAsync(Guid tenantId, Guid[] userIds)
    {
        foreach (var uid in userIds)
        {
            try { await Fx.DeleteUserInOrganizationAsync(uid); }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(
                    $"[cleanup] delete user {uid}: {ex.Message}");
            }
        }
        try { await Fx.DeleteTeamsForTenantAsync(tenantId); }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] delete teams for tenant {tenantId}: {ex.Message}");
        }
        try { await Fx.DeleteOrganizationsForTenantAsync(tenantId); }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] delete org for tenant {tenantId}: {ex.Message}");
        }
    }
#pragma warning restore CA1031

    // ---------- Test 1 -------------------------------------------------------

    /// <summary>
    /// OrgAdmin lists all members. The Member row has TeamCount == 1 (she's in a
    /// team); the OrgAdmin row has TeamCount == 0. Also confirms the response is
    /// a <see cref="CursorPage{T}"/> envelope.
    /// </summary>
    [TestMethod]
    public async Task OrgAdmin_lists_members_with_role_and_team_count()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("list-members-basic");
        var unique = Guid.NewGuid().ToString("N")[..8];

        // Seed an OrgAdmin user and a Member user.
        var orgAdminId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Org Admin-{unique}", $"orgadmin-{unique}@example.com",
            realmRole: KartovaRoles.OrgAdmin);
        var memberId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Regular Member-{unique}", $"member-{unique}@example.com",
            realmRole: KartovaRoles.Member);

        // Seed a team and add only the Member to it.
        var teamId = await Fx.SeedTeamAsync(tenantId, $"Team-{unique}");
        await Fx.SeedTeamMembershipAsync(teamId, memberId, TeamMemberRole);

        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.GetAsync(
                "/api/v1/organizations/users?sortBy=displayName&sortOrder=asc&limit=50");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                $"Expected 200. Body: {await resp.Content.ReadAsStringAsync()}");

            var page = await resp.Content.ReadFromJsonAsync<CursorPage<MemberSummaryResponse>>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page, "Response body must deserialize to CursorPage<MemberSummaryResponse>.");

            // Both seeded users should be present.
            var orgAdminRow = page!.Items.SingleOrDefault(r => r.Id == orgAdminId);
            var memberRow   = page.Items.SingleOrDefault(r => r.Id == memberId);
            Assert.IsNotNull(orgAdminRow, "OrgAdmin user must appear in the directory.");
            Assert.IsNotNull(memberRow,  "Member user must appear in the directory.");

            Assert.AreEqual(KartovaRoles.OrgAdmin, orgAdminRow!.Role,
                "OrgAdmin row must carry Role == OrgAdmin.");
            Assert.AreEqual(0, orgAdminRow.TeamCount,
                "OrgAdmin is in no teams — TeamCount must be 0.");

            Assert.AreEqual(KartovaRoles.Member, memberRow!.Role,
                "Member row must carry Role == Member.");
            Assert.AreEqual(1, memberRow.TeamCount,
                "Member is in exactly one team — TeamCount must be 1.");
        }
        finally
        {
            await CleanupAsync(tenantId, [orgAdminId, memberId]);
        }
    }

    // ---------- Test 2 -------------------------------------------------------

    /// <summary>
    /// The <c>role</c> filter narrows the result so only members whose
    /// <c>realm_role</c> equals the filter value are returned.
    /// </summary>
    [TestMethod]
    public async Task Role_filter_narrows_results()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("list-members-role-filter");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var orgAdminId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Admin User-{unique}", $"admin-rf-{unique}@example.com",
            realmRole: KartovaRoles.OrgAdmin);
        var memberId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Member User-{unique}", $"member-rf-{unique}@example.com",
            realmRole: KartovaRoles.Member);

        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.GetAsync(
                $"/api/v1/organizations/users?role={KartovaRoles.OrgAdmin}&limit=50");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                $"Expected 200. Body: {await resp.Content.ReadAsStringAsync()}");

            var page = await resp.Content.ReadFromJsonAsync<CursorPage<MemberSummaryResponse>>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);

            // Only the OrgAdmin row must be present.
            Assert.IsTrue(page!.Items.All(r => r.Role == KartovaRoles.OrgAdmin),
                "All returned rows must have Role == OrgAdmin when role=OrgAdmin filter is active.");
            Assert.IsTrue(page.Items.Any(r => r.Id == orgAdminId),
                "The seeded OrgAdmin must appear in the filtered result.");
            Assert.IsFalse(page.Items.Any(r => r.Id == memberId),
                "The seeded Member must NOT appear when filtering for OrgAdmin.");
        }
        finally
        {
            await CleanupAsync(tenantId, [orgAdminId, memberId]);
        }
    }

    // ---------- Test 3 -------------------------------------------------------

    /// <summary>
    /// A Viewer token can call <c>GET /users</c> — Viewer has
    /// <c>org.users.read</c> per <see cref="KartovaRolePermissions"/>. The
    /// response must be 200 (not 403).
    /// </summary>
    [TestMethod]
    public async Task Viewer_can_read_directory()
    {
        var (_, tenantId) = await NewTenantAsync("list-members-viewer");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var userId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"View Only-{unique}", $"viewer-{unique}@example.com",
            realmRole: KartovaRoles.Viewer);

        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.Viewer });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.GetAsync("/api/v1/organizations/users?limit=50");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                "Viewer has org.users.read and must receive 200, not 403. "
                + $"Body: {await resp.Content.ReadAsStringAsync()}");
        }
        finally
        {
            await CleanupAsync(tenantId, [userId]);
        }
    }

    // ---------- Test 5 -------------------------------------------------------

    /// <summary>
    /// The <c>q</c> infix filter narrows results: only members whose
    /// <c>display_name</c> or <c>email</c> contains the search substring are
    /// returned. Uses a >= 2-char term that matches exactly one of two seeded
    /// users (distinct display names). Also asserts that a 1-char <c>q</c>
    /// returns 422 (locks Finding 1 of the Task-4 review).
    /// </summary>
    [TestMethod]
    public async Task Q_filter_narrows_results_and_single_char_q_returns_422()
    {
        var (_, tenantId) = await NewTenantAsync("list-members-q-filter");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var zoeId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Zoe Zebra-{unique}", $"zoe-{unique}@example.com",
            realmRole: KartovaRoles.Member);
        var quentinId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Quentin Quail-{unique}", $"quentin-{unique}@example.com",
            realmRole: KartovaRoles.Member);

        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // "zeb" (>= 2 chars) should match Zoe Zebra only.
            var resp = await client.GetAsync(
                $"/api/v1/organizations/users?q=zeb&limit=50");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                $"Expected 200 for q=zeb. Body: {await resp.Content.ReadAsStringAsync()}");

            var page = await resp.Content.ReadFromJsonAsync<CursorPage<MemberSummaryResponse>>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page, "Response must deserialize to CursorPage<MemberSummaryResponse>.");

            Assert.IsTrue(page!.Items.Any(r => r.Id == zoeId),
                "Zoe Zebra must be returned when q=zeb.");
            Assert.IsFalse(page.Items.Any(r => r.Id == quentinId),
                "Quentin Quail must NOT be returned when q=zeb.");

            // A single-char q must return 422.
            var resp422 = await client.GetAsync("/api/v1/organizations/users?q=z&limit=50");
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp422.StatusCode,
                $"Expected 422 for q=z (1-char). Body: {await resp422.Content.ReadAsStringAsync()}");
        }
        finally
        {
            await CleanupAsync(tenantId, [zoeId, quentinId]);
        }
    }

    // ---------- Test 6 -------------------------------------------------------

    /// <summary>
    /// The <c>role</c> filter must be normalized case-insensitively: a lowercase
    /// <c>?role=viewer</c> must return the same results as <c>?role=Viewer</c>.
    /// Also verifies that an unknown role value returns 422 with
    /// <c>type == ProblemTypes.ValidationFailed</c>.
    /// </summary>
    [TestMethod]
    public async Task Role_filter_normalizes_case_and_unknown_role_returns_422()
    {
        var (_, tenantId) = await NewTenantAsync("list-members-role-case");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var viewerId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Viewer User-{unique}", $"viewer-rc-{unique}@example.com",
            realmRole: KartovaRoles.Viewer);
        var memberId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Member User-{unique}", $"member-rc-{unique}@example.com",
            realmRole: KartovaRoles.Member);

        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Lowercase "viewer" must normalize to "Viewer" and narrow to Viewer rows only.
            var resp = await client.GetAsync(
                $"/api/v1/organizations/users?role=viewer&limit=50");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                $"Expected 200 for role=viewer (lowercase). Body: {await resp.Content.ReadAsStringAsync()}");

            var page = await resp.Content.ReadFromJsonAsync<CursorPage<MemberSummaryResponse>>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);

            Assert.IsTrue(page!.Items.All(r => r.Role == KartovaRoles.Viewer),
                "All returned rows must have Role == Viewer when role=viewer (lowercase) filter is active.");
            Assert.IsTrue(page.Items.Any(r => r.Id == viewerId),
                "The seeded Viewer must appear in the filtered result.");
            Assert.IsFalse(page.Items.Any(r => r.Id == memberId),
                "The seeded Member must NOT appear when filtering for role=viewer.");

            // Unknown role value must return 422 with ValidationFailed problem type.
            var resp422 = await client.GetAsync("/api/v1/organizations/users?role=bogus&limit=50");
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp422.StatusCode,
                $"Expected 422 for role=bogus. Body: {await resp422.Content.ReadAsStringAsync()}");

            await using var stream = await resp422.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(ProblemTypes.ValidationFailed,
                doc.RootElement.GetProperty("type").GetString(),
                "422 response must carry ProblemTypes.ValidationFailed.");
        }
        finally
        {
            await CleanupAsync(tenantId, [viewerId, memberId]);
        }
    }

    // ---------- Test 4 -------------------------------------------------------

    /// <summary>
    /// The typeahead endpoint must still be reachable at its relocated path
    /// <c>/users/search</c> (the original <c>/users</c> path is now the
    /// directory). Uses a Member token — Member has <c>org.users.search</c>.
    /// </summary>
    [TestMethod]
    public async Task Typeahead_still_works_at_relocated_path()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("list-members-typeahead");
        var unique = Guid.NewGuid().ToString("N")[..8];

        // Display name contains a distinctive substring we'll search for (>=2 chars).
        var displayName = $"Searchable Person-{unique}";
        var userId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), displayName, $"searchable-{unique}@example.com",
            realmRole: KartovaRoles.Member);

        try
        {
            var client = Fx.CreateClient();
            // Member has org.users.search.
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.Member });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Use a >=2-char substring guaranteed to be in displayName.
            var q = Uri.EscapeDataString("Searchable");
            var resp = await client.GetAsync(
                $"/api/v1/organizations/users/search?q={q}&limit=5");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                "GET /users/search must return 200 OK at the relocated path. "
                + $"Body: {await resp.Content.ReadAsStringAsync()}");

            var body = await resp.Content.ReadFromJsonAsync<List<UserSummaryResponse>>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body, "Typeahead response must deserialize.");
            Assert.IsTrue(body!.Any(u => u.Id == userId),
                "The seeded user must appear in typeahead results matching the search term.");
        }
        finally
        {
            await CleanupAsync(tenantId, [userId]);
        }
    }

    // ---------- Test 7 (MT-1) ------------------------------------------------

    /// <summary>
    /// ADR-0095 cursor-filter-replay contract: a cursor issued under one filter set must be
    /// rejected (400 <c>cursor-filter-mismatch</c>) when replayed with a different filter —
    /// otherwise paging silently skips/duplicates rows. The members directory builds
    /// <c>expectedFilters</c> for <c>role</c>/<c>q</c> in <c>ListMembersHandler</c>; this locks it
    /// (a regression that dropped <c>expectedFilters</c> would otherwise survive every other test).
    /// </summary>
    [TestMethod]
    public async Task Cursor_replayed_with_different_role_filter_returns_400_cursor_filter_mismatch()
    {
        var (_, tenantId) = await NewTenantAsync("list-members-cursor-mismatch");
        var unique = Guid.NewGuid().ToString("N")[..8];

        // Three Members so a limit=2 page over the role=Member filter yields a NextCursor.
        var m1 = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"AAA Member-{unique}", $"m1-{unique}@example.com",
            realmRole: KartovaRoles.Member);
        var m2 = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"BBB Member-{unique}", $"m2-{unique}@example.com",
            realmRole: KartovaRoles.Member);
        var m3 = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"CCC Member-{unique}", $"m3-{unique}@example.com",
            realmRole: KartovaRoles.Member);

        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Page 1 under the role=Member filter.
            var p1 = await client.GetAsync(
                "/api/v1/organizations/users?role=Member&sortBy=displayName&sortOrder=asc&limit=2");
            Assert.AreEqual(HttpStatusCode.OK, p1.StatusCode,
                $"Expected 200 for page 1. Body: {await p1.Content.ReadAsStringAsync()}");

            var page1 = await p1.Content.ReadFromJsonAsync<CursorPage<MemberSummaryResponse>>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page1);
            Assert.IsNotNull(page1!.NextCursor,
                "limit=2 over 3 matching members must yield a NextCursor to replay.");

            // Replay the cursor but flip the role filter → must be rejected, not silently served.
            var mismatch = await client.GetAsync(
                "/api/v1/organizations/users?role=OrgAdmin&sortBy=displayName&sortOrder=asc&limit=2"
                + $"&cursor={Uri.EscapeDataString(page1.NextCursor!)}");

            Assert.AreEqual(HttpStatusCode.BadRequest, mismatch.StatusCode,
                $"Replaying a cursor with a changed filter must return 400. Body: {await mismatch.Content.ReadAsStringAsync()}");

            await using var stream = await mismatch.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(ProblemTypes.CursorFilterMismatch,
                doc.RootElement.GetProperty("type").GetString(),
                "400 response must carry ProblemTypes.CursorFilterMismatch.");
        }
        finally
        {
            await CleanupAsync(tenantId, [m1, m2, m3]);
        }
    }

    // ---------- Test 8 (MT-3) ------------------------------------------------

    /// <summary>
    /// The team-count enrichment must aggregate across <em>all</em> of a member's teams, not
    /// cap at 1. A member in two teams must report <c>TeamCount == 2</c> (a mutant collapsing the
    /// <c>GROUP BY … COUNT</c> to a constant 1 survives the single-team Test 1).
    /// </summary>
    [TestMethod]
    public async Task Member_in_two_teams_reports_team_count_of_two()
    {
        var (_, tenantId) = await NewTenantAsync("list-members-teamcount-multi");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var memberId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Multi Team-{unique}", $"multi-{unique}@example.com",
            realmRole: KartovaRoles.Member);

        var teamA = await Fx.SeedTeamAsync(tenantId, $"TeamA-{unique}");
        var teamB = await Fx.SeedTeamAsync(tenantId, $"TeamB-{unique}");
        await Fx.SeedTeamMembershipAsync(teamA, memberId, TeamMemberRole);
        await Fx.SeedTeamMembershipAsync(teamB, memberId, TeamMemberRole);

        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.GetAsync(
                "/api/v1/organizations/users?sortBy=displayName&sortOrder=asc&limit=50");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                $"Expected 200. Body: {await resp.Content.ReadAsStringAsync()}");

            var page = await resp.Content.ReadFromJsonAsync<CursorPage<MemberSummaryResponse>>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);

            var row = page!.Items.SingleOrDefault(r => r.Id == memberId);
            Assert.IsNotNull(row, "The multi-team member must appear in the directory.");
            Assert.AreEqual(2, row!.TeamCount,
                "Member belongs to two teams — TeamCount must aggregate to 2, not cap at 1.");
        }
        finally
        {
            await CleanupAsync(tenantId, [memberId]);
        }
    }
}
