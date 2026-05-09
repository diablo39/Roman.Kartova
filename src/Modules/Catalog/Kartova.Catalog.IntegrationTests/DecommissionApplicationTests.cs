using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class DecommissionApplicationTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    [TestMethod]
    public async Task POST_decommission_when_Deprecated_and_past_sunsetDate_returns_200_and_sets_lifecycle_to_Decommissioned()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "decommission-app-1", "Decommission App 1", "Desc.");

        // Deprecate with sunsetDate one second in the future, then sleep past it.
        // The Task.Delay(2000) crosses the boundary so Application.Decommission's
        // "now >= sunsetDate" guard accepts the transition. Plan §Task 13 acknowledges
        // the small flakiness risk; FakeTimeProvider override is a future improvement.
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        await Task.Delay(2000);

        // Empty-body POST. PostAsync(url, null) sends no Content-Type/Content-Length;
        // the endpoint binding has no [FromBody] parameter so the framework binds the
        // path id, ignores the absent body, and dispatches.
        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(Lifecycle.Decommissioned, body!.Lifecycle);
        // The stored sunsetDate is preserved on Decommission — only the lifecycle column flips.
        Assert.IsNotNull(body.SunsetDate);
    }

    [TestMethod]
    public async Task POST_decommission_when_Deprecated_and_before_sunsetDate_returns_409_with_reason_before_sunset_date()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "decommission-app-2", "Decommission App 2", "Desc.");

        // Deprecate with a far-future sunsetDate, then immediately try to decommission
        // — the "now < sunsetDate" branch of Application.Decommission throws
        // InvalidLifecycleTransitionException(reason="before-sunset-date") which the
        // shared handler maps to 409 with both `reason` and `sunsetDate` extensions.
        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
        Assert.AreEqual("before-sunset-date", problem.Reason);
        Assert.AreEqual("deprecated", problem.CurrentLifecycle);
        Assert.AreEqual("Decommission", problem.AttemptedTransition);
        // SunsetDate extension carries the originally-stored value so the client knows
        // when the transition would become valid. Round-trip JSON precision tolerance
        // mirrors DeprecateApplicationTests.
        Assert.IsNotNull(problem.SunsetDate);
        var diff = (problem.SunsetDate!.Value - sunset).Duration();
        Assert.IsTrue(diff <= TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task POST_decommission_when_Active_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "decommission-app-3", "Decommission App 3", "Desc.");

        // Skip the deprecate step — the freshly-registered app is Active. The
        // "current state must be Deprecated" invariant rejects with no reason
        // attached (no sunsetDate either, since the entity has none).
        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
        Assert.AreEqual("active", problem.CurrentLifecycle);
        Assert.AreEqual("Decommission", problem.AttemptedTransition);
        Assert.IsNull(problem.Reason);
    }

    [TestMethod]
    public async Task POST_decommission_when_already_Decommissioned_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "decommission-app-4", "Decommission App 4", "Desc.");

        // Drive through Active → Deprecated → Decommissioned, then try one more
        // decommission to land on the wrong-source-state branch.
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        await Task.Delay(2000);
        var firstDecommission = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);
        Assert.IsTrue(firstDecommission.IsSuccessStatusCode);

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
        Assert.AreEqual("decommissioned", problem.CurrentLifecycle);
        Assert.AreEqual("Decommission", problem.AttemptedTransition);
    }

    [TestMethod]
    public async Task POST_decommission_for_other_tenants_id_returns_404()
    {
        var orgAClient = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var orgARegistered = await RegisterAsync(orgAClient, "decommission-app-5", "App", "Desc.");
        // Deprecate as OrgA so the row is in the right state for the (different) tenant
        // to attempt decommission. RLS hides the row from OrgB so the handler returns
        // null → 404, identical to "no such row".
        var deprecateResp = await orgAClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{orgARegistered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var orgBClient = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await orgBClient.PostAsync(
            $"/api/v1/catalog/applications/{orgARegistered.Id}/decommission",
            content: null);

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private static async Task<ApplicationResponse> RegisterAsync(HttpClient client, string name, string displayName, string description)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));
        Assert.IsTrue(resp.IsSuccessStatusCode);
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    // Mirrors the ProblemPayload nested in EditApplicationTests / DeprecateApplicationTests
    // and adds the Reason + SunsetDate top-level extensions that LifecycleConflictExceptionHandler
    // attaches on the "before-sunset-date" path. Acceptable duplication for slice 5; a shared
    // helper could be extracted later if a fourth caller appears.
    //
    // System.Text.Json deserialises top-level extension members by NAME against this class's
    // properties — anything we want to assert on must be declared here. Unknown members are
    // dropped silently.
    private sealed class ProblemPayload
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Status { get; set; }
        public string? Detail { get; set; }

        public Dictionary<string, string[]> Errors { get; set; } = new();

        // Top-level extensions populated by LifecycleConflictExceptionHandler.
        public string? CurrentVersion { get; set; }
        public string? CurrentLifecycle { get; set; }
        public string? AttemptedTransition { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset? SunsetDate { get; set; }
    }
}
