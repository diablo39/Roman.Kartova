using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

public class AdminBypassTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture _fx;

    public AdminBypassTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Platform_admin_can_create_organization_without_tenant_scope()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _fx.Signer.IssueForPlatformAdmin());

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = "Newly created" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await resp.Content.ReadFromJsonAsync<OrganizationDto>();
        dto!.Name.Should().Be("Newly created");
        dto.Id.Should().Be(dto.TenantId);
    }

    [Fact]
    public async Task Non_platform_admin_cannot_post_admin_organizations()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" }));

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = "Denied" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
