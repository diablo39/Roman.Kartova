using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

public class OrganizationEndpointHappyPathTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture _fx;

    public OrganizationEndpointHappyPathTests(KartovaApiFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task Get_me_returns_current_tenant_row()
    {
        await _fx.RunMigrationsAsync();
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgB.Value, "Org B");

        var client = _fx.CreateClient();
        var token = _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/organizations/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<OrganizationDto>();
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(SeededOrgs.OrgA.Value);
        dto.Name.Should().Be("Org A");
    }
}
