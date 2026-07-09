using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Npgsql;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class RelationshipTypeHardeningTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static object Rel(EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid) =>
        new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid };

    // Insert a relationship row whose `type` is not in the RelationshipType enum,
    // simulating drifted/legacy data (the removed 'PartOf' value). Uses the
    // RLS-bypass connection so we can write the row for OrgA's tenant directly.
    private async Task InsertDriftRowAsync(Guid tenantId, Guid sourceId, Guid targetId)
    {
        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO relationships
              (id, tenant_id, source_kind, source_id, target_kind, target_id, type, origin, created_by_user_id, created_at)
            VALUES (gen_random_uuid(), $1, 'Service', $2, 'Service', $3, 'PartOf', 'Manual', gen_random_uuid(), now());
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(sourceId);
        cmd.Parameters.AddWithValue(targetId);
        await cmd.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task Unknown_type_row_is_excluded_and_does_not_500_the_relationships_surface()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Hardening Team");

        // A known edge that MUST still be returned.
        var svcA = await SeedServiceAsync(client, teamId, "harden-a");
        var svcB = await SeedServiceAsync(client, teamId, "harden-b");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svcA, RelationshipType.DependsOn, EntityKind.Service, svcB));

        // A drift row that would previously throw at EF materialization → 500.
        await InsertDriftRowAsync(tenantId.Value, svcA, svcB);

        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={svcA}&direction=all");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, "unknown-type row must not 500 the surface");
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(1, page!.Items.Count, "only the known DependsOn edge is returned; drift row excluded");
        Assert.AreEqual(RelationshipType.DependsOn, page.Items[0].Type);
    }

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = name, description = "x", teamId, endpoints = Array.Empty<object>(),
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}': {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }
}
