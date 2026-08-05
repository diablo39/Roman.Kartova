using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Fail-fast checkpoint proving <see cref="CurrentMembershipQueries.SystemsForComponentsAsync"/>
/// actually translates and executes against real Postgres. Every query in
/// <c>CurrentMembershipQueries</c> reads <c>Relationship.Source</c>/<c>.Target</c>, which are EF
/// ComplexProperties — a translation failure there is unconditional on data (throws
/// <see cref="InvalidOperationException"/> at query-compile time regardless of what's seeded), so
/// discovering it here costs one seeded row instead of surfacing as a 500 further downstream.
/// <para>
/// Endpoint-level <c>?systemId=</c> smoke tests (the correlated <c>EXISTS</c> shape built on
/// <see cref="CurrentMembershipQueries.IsMemberOfAnySystem{TComponent}"/>) live elsewhere
/// (<c>ListBySystemFilterTests</c>); this class covers only the direct
/// <see cref="CurrentMembershipQueries.SystemsForComponentsAsync"/> call.
/// </para>
/// </summary>
[TestClass]
public sealed class SystemEnrichmentTranslationTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    [TestMethod]
    public async Task Batched_system_lookup_resolves_a_seeded_membership()
    {
        // Primarily a TRANSLATION check — a failure throws InvalidOperationException at
        // query-compile time regardless of data. But seed one real edge anyway: asserting
        // "empty for a random GUID" would also pass if the method returned empty for every
        // input, or if RLS (not the code) produced the empty result.
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "A2 Translation Probe Team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, "A2 Translation Probe");
        var appId = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, "a2-xlate-probe");
        await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appId, systemId);

        await using var db = Fx.CatalogDb();
        var result = await CurrentMembershipQueries.SystemsForComponentsAsync(
            db, EntityKind.Application, [appId], CancellationToken.None);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(systemId, result[appId].Id);
        Assert.AreEqual("A2 Translation Probe", result[appId].DisplayName);
    }
}
