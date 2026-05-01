using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for GET /api/v1/organizations/me/admin-only.
///
/// Primary goal: kill the Stryker Block-removal mutant on
/// OrganizationEndpointDelegates.cs line 25. The body-shape assertion
/// ensures the endpoint body executes; replacing it with {} causes
/// default(IResult) semantics — the response body will be empty or null
/// and <c>body!.Message.Should().Be("ok")</c> will fail.
/// </summary>
[Collection(KartovaApiCollection.Name)]
public class OrganizationAdminOnlyEndpointTests
{
    private readonly KartovaApiFixture _fx;

    public OrganizationAdminOnlyEndpointTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task GetAdminOnly_returns_200_with_message_ok()
    {
        // Arrange — OrgAdmin role satisfies RequireRole(KartovaRoles.OrgAdmin).
        var client = _fx.CreateClient();
        var token = _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/organizations/me/admin-only");

        // Assert status
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert body shape — this is the mutant-kill assertion:
        // if the endpoint body is removed (Stryker Block removal), the method returns
        // default(IResult), which produces an empty or null response body; in that
        // case ReadFromJsonAsync returns null and the Be("ok") assertion fails, killing
        // the mutant.
        var body = await response.Content.ReadFromJsonAsync<AdminOnlyResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("ok");
    }

    [Fact]
    public async Task GetAdminOnly_without_authentication_returns_401()
    {
        // Arrange — no Authorization header (anonymous client).
        var client = _fx.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/organizations/me/admin-only");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAdminOnly_with_member_role_returns_403()
    {
        // Arrange — Member does NOT have OrgAdmin role; route requires OrgAdmin.
        var client = _fx.CreateClient();
        var token = _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "Member" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/organizations/me/admin-only");

        // Assert — RBAC is wired via RequireRole(KartovaRoles.OrgAdmin); a Member token
        // is authenticated but not authorized, so the response must be 403 Forbidden.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
