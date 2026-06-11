using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for the slice-9 user typeahead search endpoint
/// (<c>GET /api/v1/organizations/users?q=...&amp;limit=...</c> — spec §6.7 /
/// §11.3 scenarios #13 + #14). End-to-end against the real Keycloak + Postgres
/// container pair wired by the Organization fixture (Keycloak isn't exercised
/// here — user search reads the local <c>users</c> projection — but the shared
/// fixture keeps the suite cohesive with the H1 invitation flow). DB
/// verification rides through the endpoint's HTTP response since the search
/// projection already returns the visible row set.
/// </summary>
[TestClass]
public sealed class UserSearchTests : OrganizationIntegrationTestBase
{
    /// <summary>
    /// Best-effort teardown for a single-tenant user-search test. Deletes each
    /// seeded <c>users</c> row by id first (slice-9 e5aaf73 convention — direct-id
    /// no-prefix-sweep deletes ride first so a later organizations cleanup throw
    /// can't strand them), then drops the <c>organizations</c> row. Each step
    /// runs in its own try/catch so a throw on one does not skip the others.
    /// Errors go to <c>Console.Error</c> so a CI failure surfaces the cleanup
    /// gap without masking the original test failure that fired the
    /// <c>finally</c>.
    /// </summary>
    private static async Task CleanupTenantUsersAsync(Guid tenantId, params Guid[] userIds)
    {
#pragma warning disable CA1031 // best-effort test teardown — log and continue
        foreach (var userId in userIds)
        {
            try
            {
                await Fx.DeleteUserInOrganizationAsync(userId);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(
                    $"[cleanup] users delete failed for user {userId}: {ex.Message}");
            }
        }

        try
        {
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] organizations delete failed for tenant {tenantId}: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    // ---------- Scenario #13: typeahead matches display-name AND email -------

    [TestMethod]
    public async Task User_search_typeahead_matches_displayname_and_email()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("user-search-displayname");

        // Seed in NON-alphabetical insertion order (Carol, Alice, Bob) so the
        // OrderBy(DisplayName) is observable on the `?q=example` ordering
        // assertion below — a mutant that drops the OrderBy would return rows
        // in seed order, which is detectably different.
        // All three emails share the substring "example" so a single query
        // (`?q=example`) returns all three and we can assert alphabetical
        // order. Each display name is also constructed so that a query like
        // "wonderland" hits ONLY the display name (no email contains it),
        // proving display-name match is independent of email match.
        var carolId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), "Carol Davis", "carol@three.example.org");
        var aliceId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), "Alice Wonderland", "alice@one.example.org");
        var bobId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), "Bob Smith", "bob@two.example.org");

        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            // ----- Display-name match (independent of email predicate) -----
            // "wonderland" appears only in Alice's display name; no email
            // contains it. If the OR predicate were reduced to email-only this
            // assertion would return 0 results, killing that mutant.
            var aliceOnly = await GetSearchResultsAsync(client, "wonderland");
            Assert.AreEqual(1, aliceOnly.Count,
                "`q=wonderland` must match Alice's display name and only Alice.");
            Assert.AreEqual(aliceId, aliceOnly[0].Id);
            Assert.AreEqual("Alice Wonderland", aliceOnly[0].DisplayName);

            // ----- Case-insensitive match -----
            // "SMITH" is uppercase but "Bob Smith" is mixed case. The query is
            // lowercased via ToLowerInvariant() and compared against
            // DisplayName.ToLower() — removing either ToLower call would fail
            // this assertion.
            var bobOnly = await GetSearchResultsAsync(client, "SMITH");
            Assert.AreEqual(1, bobOnly.Count,
                "`q=SMITH` must case-insensitively match Bob's display name.");
            Assert.AreEqual(bobId, bobOnly[0].Id);

            // ----- Email match (independent of display-name predicate) -----
            // "three" appears only in Carol's email "carol@three.example.org";
            // no display name contains "three". If the OR predicate were
            // reduced to display-name-only this assertion would return 0
            // results, killing that mutant.
            var carolByEmail = await GetSearchResultsAsync(client, "three");
            Assert.AreEqual(1, carolByEmail.Count,
                "`q=three` must match Carol's email and only Carol.");
            Assert.AreEqual(carolId, carolByEmail[0].Id);
            Assert.AreEqual("carol@three.example.org", carolByEmail[0].Email);

            // ----- Substring (not prefix) match -----
            // "arol" is a mid-string fragment of "Carol Davis" — it would NOT
            // match a prefix-only predicate (StartsWith). Killing the
            // "tightened to StartsWith" mutant.
            var carolByMidString = await GetSearchResultsAsync(client, "arol");
            Assert.AreEqual(1, carolByMidString.Count,
                "`q=arol` must match the middle of 'Carol' — proves substring, not prefix.");
            Assert.AreEqual(carolId, carolByMidString[0].Id);

            // ----- Ordering: results alphabetical by DisplayName -----
            // "example" hits all three emails. Insertion order was
            // Carol → Alice → Bob, but the response must be Alice, Bob, Carol
            // (alphabetical by display name). A mutant that drops the OrderBy
            // clause would yield insertion order; a mutant that sorts by
            // Email or by Id would shuffle the result differently. Both are
            // killed by the strict three-position assertion.
            var allThree = await GetSearchResultsAsync(client, "example");
            Assert.AreEqual(3, allThree.Count,
                "`q=example` must return exactly 3 results (the seeded set, no extras).");
            CollectionAssert.AreEqual(
                new[] { aliceId, bobId, carolId },
                allThree.Select(u => u.Id).ToArray(),
                "Results must be ordered alphabetically by DisplayName "
                + "(Alice → Bob → Carol), independent of insertion order.");
        }
        finally
        {
            await CleanupTenantUsersAsync(tenantId, carolId, aliceId, bobId);
        }
    }

    // ---------- Scenario #14: tenant scoping via RLS ------------------------

    [TestMethod]
    public async Task User_search_is_tenant_scoped_by_rls()
    {
        // Two distinct admin domains → two distinct deterministic tenants. Both
        // tenants seed users whose display names contain the substring "match"
        // so a single query (`?q=match`) would return all four IF RLS were
        // disabled — making the test sensitive to a regression that drops the
        // tenant_id row-level policy.
        var (adminAEmail, tenantA) = await NewTenantAsync("user-search-rls-a");
        var (adminBEmail, tenantB) = await NewTenantAsync("user-search-rls-b");
        Assert.AreNotEqual(tenantA, tenantB,
            "Distinct admin domains MUST yield distinct deterministic tenants.");

        var aliceA = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantA), "Search Match A1", "match-a1@a.kartova.local");
        var bettyA = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantA), "Search Match A2", "match-a2@a.kartova.local");
        var aliceB = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantB), "Search Match B1", "match-b1@b.kartova.local");
        var bettyB = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantB), "Search Match B2", "match-b2@b.kartova.local");

        try
        {
            // ----- Tenant A admin sees ONLY tenant A's two users -----
            var clientA = await Fx.CreateAuthenticatedClientAsync(
                adminAEmail, new[] { KartovaRoles.OrgAdmin });
            var fromA = await GetSearchResultsAsync(clientA, "match");
            Assert.AreEqual(2, fromA.Count,
                "Tenant A's admin must see exactly 2 matches (A's users only). "
                + "A 4-result response would prove RLS isn't filtering.");
            // ID-level assertion (not just count) — a coincidental 2 from
            // tenant B would slip past a count-only check; this kills that
            // weakness. OrderBy(DisplayName) → A1 before A2.
            CollectionAssert.AreEqual(
                new[] { aliceA, bettyA },
                fromA.Select(u => u.Id).ToArray(),
                "Tenant A's results must be the A-seeded user ids, not B's.");

            // ----- Tenant B admin sees ONLY tenant B's two users -----
            // Both directions tested — catches a regression where only one
            // tenant is queryable (e.g., a half-applied RLS policy).
            var clientB = await Fx.CreateAuthenticatedClientAsync(
                adminBEmail, new[] { KartovaRoles.OrgAdmin });
            var fromB = await GetSearchResultsAsync(clientB, "match");
            Assert.AreEqual(2, fromB.Count,
                "Tenant B's admin must see exactly 2 matches (B's users only).");
            CollectionAssert.AreEqual(
                new[] { aliceB, bettyB },
                fromB.Select(u => u.Id).ToArray(),
                "Tenant B's results must be the B-seeded user ids, not A's.");
        }
        finally
        {
            await CleanupTenantUsersAsync(tenantA, aliceA, bettyA);
            await CleanupTenantUsersAsync(tenantB, aliceB, bettyB);
        }
    }

    // ---------- helpers ------------------------------------------------------

    // ---------- MT5 (slice-9 carry-forward): typeahead query validation ------

    [TestMethod]
    public async Task User_search_empty_query_returns_422_ValidationFailed()
    {
        // The endpoint trims `q` and rejects empty/whitespace-only with 422 +
        // type=validation-failed + detail "Query 'q' is required.".
        // Distinct from the "too short" branch below.
        var (adminEmail, tenantId) = await NewTenantAsync("user-search-empty");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            var resp = await client.GetAsync("/api/v1/organizations/users/search?q=");
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(
                ProblemTypes.ValidationFailed,
                doc.RootElement.GetProperty("type").GetString());
            var detail = doc.RootElement.GetProperty("detail").GetString();
            Assert.IsNotNull(detail);
            StringAssert.Contains(detail!, "required",
                "Empty `q` must surface the 'required' branch — not the 'too short' branch.");
        }
        finally
        {
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }

    [TestMethod]
    public async Task User_search_single_char_query_returns_422_ValidationFailed()
    {
        // The endpoint's "at least 2 characters" branch fires for trimmed
        // single-character queries. Detail must contain "at least 2 characters"
        // so this kills any mutant that swaps the two 422 branches.
        var (adminEmail, tenantId) = await NewTenantAsync("user-search-short");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            var resp = await client.GetAsync("/api/v1/organizations/users/search?q=a");
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(
                ProblemTypes.ValidationFailed,
                doc.RootElement.GetProperty("type").GetString());
            var detail = doc.RootElement.GetProperty("detail").GetString();
            Assert.IsNotNull(detail);
            StringAssert.Contains(detail!, "at least 2 characters",
                "Single-char `q` must surface the 'too short' branch with the exact threshold message.");
        }
        finally
        {
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }

    /// <summary>
    /// Issues a typed GET against the user-search endpoint and deserializes
    /// the response with the project's <see cref="KartovaApiFixtureBase.WireJson"/>
    /// options (camelCase + JsonStringEnumConverter). Asserts the response is
    /// 200 OK so caller assertions can focus on shape and contents. Returns an
    /// IReadOnlyList of <see cref="UserSummaryResponse"/> — the endpoint
    /// returns a bare JSON array (no envelope).
    /// </summary>
    private static async Task<IReadOnlyList<UserSummaryResponse>> GetSearchResultsAsync(
        HttpClient client, string q)
    {
        // Uri.EscapeDataString protects against future test cases where `q`
        // contains spaces or other URL-reserved characters; current cases are
        // ASCII-safe but the helper stays defensive.
        var resp = await client.GetAsync(
            $"/api/v1/organizations/users/search?q={Uri.EscapeDataString(q)}");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
            $"Search `q={q}` must return 200 OK.");
        var body = await resp.Content.ReadFromJsonAsync<List<UserSummaryResponse>>(
            KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(body, "Search response body must deserialize to a non-null list.");
        return body!;
    }
}
