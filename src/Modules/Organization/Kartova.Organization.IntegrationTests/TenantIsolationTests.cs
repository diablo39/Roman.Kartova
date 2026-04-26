using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;
using Npgsql;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

[Collection(KartovaApiCollection.Name)]
public class TenantIsolationTests
{
    private readonly KartovaApiFixture _fx;

    public TenantIsolationTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Each_tenant_only_sees_its_own_organization()
    {
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgB.Value, "Org B");

        var client = _fx.CreateClient();

        var tokenA = _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var respA = await client.GetAsync("/api/v1/organizations/me");
        respA.StatusCode.Should().Be(HttpStatusCode.OK);
        (await respA.Content.ReadFromJsonAsync<OrganizationDto>())!.Name.Should().Be("Org A");

        var tokenB = _fx.Signer.IssueForTenant(SeededOrgs.OrgB, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var respB = await client.GetAsync("/api/v1/organizations/me");
        respB.StatusCode.Should().Be(HttpStatusCode.OK);
        (await respB.Content.ReadFromJsonAsync<OrganizationDto>())!.Name.Should().Be("Org B");
    }

    [Fact]
    public async Task Raw_sql_as_bypass_role_sees_both_rows()
    {
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        await _fx.SeedOrganizationAsync(SeededOrgs.OrgB.Value, "Org B");

        await using var conn = new NpgsqlConnection(_fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM organizations";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().BeGreaterThanOrEqualTo(2);
    }
}
