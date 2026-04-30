using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Negative-path coverage for the Organization endpoints — slice-3 §13.11. The
/// happy paths sit in <see cref="OrganizationEndpointHappyPathTests"/> and
/// <see cref="AdminBypassTests"/>; the validation 400 + 404 branches were the
/// largest line-coverage gap in <c>OrganizationEndpointDelegates</c> (40%) and
/// <c>AdminOrganizationEndpointDelegates</c> (33.3%) at the slice-3 boundary.
/// </summary>
[Collection(KartovaApiCollection.Name)]
public class OrganizationEndpointNegativePathTests
{
    private readonly KartovaApiFixture _fx;

    public OrganizationEndpointNegativePathTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Get_me_returns_404_problem_details_when_tenant_has_no_visible_org()
    {
        // Use a fresh deterministic tenant id that has not been seeded — the GET /me
        // handler returns Results.Problem(ResourceNotFound, 404).
        var emptyTenant = new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid());

        var client = _fx.CreateClient();
        var token = _fx.Signer.IssueForTenant(emptyTenant, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/organizations/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resource-not-found");
        body.Should().Contain("Organization not found");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Admin_create_returns_400_when_name_is_blank(string blank)
    {
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _fx.Signer.IssueForPlatformAdmin());

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = blank });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("validation-failed");
        body.Should().Contain("Name must not be empty");
    }

    [Fact]
    public async Task Admin_create_returns_400_when_name_exceeds_max_length()
    {
        var overLength = new string('x', 101); // NameMaxLength is 100

        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _fx.Signer.IssueForPlatformAdmin());

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = overLength });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("validation-failed");
        body.Should().Contain("100 characters or fewer");
    }

    [Fact]
    public async Task Admin_create_succeeds_at_exact_max_length_boundary()
    {
        // Pin the boundary: 100 chars exactly must succeed (kills `length >= 100` mutant).
        var exactBoundary = new string('x', 100);

        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _fx.Signer.IssueForPlatformAdmin());

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = exactBoundary });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
