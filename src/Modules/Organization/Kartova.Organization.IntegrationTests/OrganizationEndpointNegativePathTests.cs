using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Negative-path coverage for the Organization endpoints — slice-3 §13.11. The
/// happy paths sit in <see cref="OrganizationEndpointHappyPathTests"/> and
/// <see cref="AdminBypassTests"/>; the validation 400 + 404 branches were the
/// largest line-coverage gap in <c>OrganizationEndpointDelegates</c> (40%) and
/// <c>AdminOrganizationEndpointDelegates</c> (33.3%) at the slice-3 boundary.
/// </summary>
[TestClass]
public class OrganizationEndpointNegativePathTests : OrganizationIntegrationTestBase
{
    [TestMethod]
    public async Task Get_me_returns_404_problem_details_when_tenant_has_no_visible_org()
    {
        // Use a fresh deterministic tenant id that has not been seeded — the GET /me
        // handler returns Results.Problem(ResourceNotFound, 404).
        var emptyTenant = new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid());

        var client = Fx.CreateClient();
        var token = Fx.Signer.IssueForTenant(emptyTenant, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/organizations/me");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "resource-not-found");
        StringAssert.Contains(body, "Organization not found");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task Admin_create_returns_400_when_name_is_blank(string blank)
    {
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForPlatformAdmin());

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = blank });

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "validation-failed");
        StringAssert.Contains(body, "Name must not be empty");
    }

    [TestMethod]
    public async Task Admin_create_returns_400_when_name_exceeds_max_length()
    {
        var overLength = new string('x', 101); // NameMaxLength is 100

        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForPlatformAdmin());

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = overLength });

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "validation-failed");
        StringAssert.Contains(body, "100 characters or fewer");
    }

    [TestMethod]
    public async Task Admin_create_succeeds_at_exact_max_length_boundary()
    {
        // Pin the boundary: 100 chars exactly must succeed (kills `length >= 100` mutant).
        var exactBoundary = new string('x', 100);

        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForPlatformAdmin());

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = exactBoundary });

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
    }
}
