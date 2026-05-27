using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class UnDecommissionApplicationTests : CatalogIntegrationTestBase
{
    private const string OrgAdminEmail = "admin@orga.kartova.local";
    private const string MemberEmail   = "member@orga.kartova.local";

    [TestMethod]
    public async Task POST_un_decommission_from_Decommissioned_returns_200_with_Deprecated_state_and_new_sunsetDate()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registered = await RegisterAsync(client, "UnDecommission App 1", "Desc.");

        // Active → Deprecated (with near-future sunset) → Decommissioned (wait past sunset).
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        await Task.Delay(2000);

        var decommissionResp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);
        Assert.IsTrue(decommissionResp.IsSuccessStatusCode);

        // Now un-decommission with a new future sunsetDate.
        var newSunset = DateTimeOffset.UtcNow.AddDays(30);
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/un-decommission",
            new { sunsetDate = newSunset });

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(Lifecycle.Deprecated, body!.Lifecycle);
        Assert.IsNotNull(body.SunsetDate);
        var diff = (body.SunsetDate!.Value - newSunset).Duration();
        Assert.IsTrue(diff <= TimeSpan.FromSeconds(1));

        // Follow-up GET — confirms the lifecycle change persisted across the request boundary
        // (kills the SaveChangesAsync-removed mutation: without persistence the in-memory state
        // would be lost on a second request).
        var followUp = await client.GetAsync($"/api/v1/catalog/applications/{registered.Id}");
        Assert.AreEqual(HttpStatusCode.OK, followUp.StatusCode);
        var persisted = await followUp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(Lifecycle.Deprecated, persisted!.Lifecycle);
        Assert.IsNotNull(persisted.SunsetDate);
        var persistedDiff = (persisted.SunsetDate!.Value - newSunset).Duration();
        Assert.IsTrue(persistedDiff <= TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task POST_un_decommission_from_Deprecated_returns_409_lifecycle_conflict()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registered = await RegisterAsync(client, "UnDecommission App 2", "Desc.");

        // Deprecate (Active → Deprecated), then immediately try un-decommission.
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/un-decommission",
            new { sunsetDate = DateTimeOffset.UtcNow.AddDays(60) });

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
    }

    [TestMethod]
    public async Task POST_un_decommission_from_Active_returns_409_lifecycle_conflict()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registered = await RegisterAsync(client, "UnDecommission App 3", "Desc.");

        // Freshly-registered app is Active — wrong source state.
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/un-decommission",
            new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
    }

    [TestMethod]
    public async Task POST_un_decommission_with_past_sunsetDate_returns_400_validation_failed()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registered = await RegisterAsync(client, "UnDecommission App 4", "Desc.");

        // Drive to Decommissioned state first.
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        await Task.Delay(2000);

        var decommissionResp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);
        Assert.IsTrue(decommissionResp.IsSuccessStatusCode);

        // Send a past sunsetDate — the domain's ArgumentException maps to 400.
        var pastDate = DateTimeOffset.UtcNow.AddDays(-1);
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/un-decommission",
            new { sunsetDate = pastDate });

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.ValidationFailed, problem!.Type);
        Assert.IsTrue(problem.Errors.ContainsKey("newSunsetDate"));
    }

    [TestMethod]
    public async Task POST_un_decommission_as_Member_returns_403()
    {
        var orgAdminClient = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registered = await RegisterAsync(orgAdminClient, "UnDecommission App 5", "Desc.");

        // Drive to Decommissioned.
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        await orgAdminClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        await Task.Delay(2000);
        await orgAdminClient.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        var memberClient = await Fx.CreateAuthenticatedClientAsync(MemberEmail, new[] { KartovaRoles.Member });
        var resp = await memberClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/un-decommission",
            new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_un_decommission_unauthenticated_returns_401()
    {
        var client = Fx.CreateAnonymousClient();
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{Guid.NewGuid()}/un-decommission",
            new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_un_decommission_for_other_tenants_id_returns_404()
    {
        // Register, deprecate, and decommission as OrgB so the app is in an un-decommissionable state.
        var orgBClient = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local", new[] { KartovaRoles.OrgAdmin });
        var otherTenantApp = await RegisterAsync(orgBClient, "Cross Tenant UnDecommission", "Desc.");

        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecateResp = await orgBClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{otherTenantApp.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode, $"OrgB deprecate must succeed (was {deprecateResp.StatusCode}).");

        await Task.Delay(2000);

        var decommissionResp = await orgBClient.PostAsync(
            $"/api/v1/catalog/applications/{otherTenantApp.Id}/decommission",
            content: null);
        Assert.IsTrue(decommissionResp.IsSuccessStatusCode, $"OrgB decommission must succeed (was {decommissionResp.StatusCode}).");

        // Attempt to un-decommission OrgB's app as OrgA — RLS filters the cross-tenant row → 404.
        var orgAClient = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var resp = await orgAClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{otherTenantApp.Id}/un-decommission",
            new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private static async Task<ApplicationResponse> RegisterAsync(HttpClient client, string displayName, string description)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(displayName, description));
        Assert.IsTrue(resp.IsSuccessStatusCode);
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    // Mirrors ProblemPayload from DeprecateApplicationTests / DecommissionApplicationTests.
    // Includes the Errors dictionary for the 400 ValidationFailed assertion and the
    // Type/CurrentLifecycle fields for the 409 LifecycleConflict assertions.
    private sealed class ProblemPayload
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Status { get; set; }
        public string? Detail { get; set; }

        public Dictionary<string, string[]> Errors { get; set; } = new();

        public string? CurrentLifecycle { get; set; }
        public string? AttemptedTransition { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset? SunsetDate { get; set; }
    }
}
