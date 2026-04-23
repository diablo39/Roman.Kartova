using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

public class AuthErrorTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture _fx;

    public AuthErrorTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task No_token_returns_401()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v1/organizations/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _fx.Signer.IssueExpired(SeededOrgs.OrgA));
        var resp = await client.GetAsync("/api/v1/organizations/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Platform_admin_without_tenant_hits_missing_tenant_on_tenant_scoped_route()
    {
        await _fx.RunMigrationsAsync();
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _fx.Signer.IssueForPlatformAdmin());
        var resp = await client.GetAsync("/api/v1/organizations/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("missing-tenant-claim");
    }

    [Fact]
    public async Task Non_org_admin_gets_403_on_admin_only_endpoint()
    {
        await _fx.RunMigrationsAsync();
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "Member" }));
        var resp = await client.GetAsync("/api/v1/organizations/me/admin-only");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
