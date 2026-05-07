using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[Collection(KartovaApiCollection.Name)]
public class DecommissionApplicationTests
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private readonly KartovaApiFixture _fx;

    public DecommissionApplicationTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task POST_decommission_when_Deprecated_and_past_sunsetDate_returns_200_and_sets_lifecycle_to_Decommissioned()
    {
        var client = await _fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "decommission-app-1", "Decommission App 1", "Desc.");

        // Deprecate with sunsetDate one second in the future, then sleep past it.
        // The Task.Delay(2000) crosses the boundary so Application.Decommission's
        // "now >= sunsetDate" guard accepts the transition. Plan §Task 13 acknowledges
        // the small flakiness risk; FakeTimeProvider override is a future improvement.
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        deprecateResp.IsSuccessStatusCode.Should().BeTrue();

        await Task.Delay(2000);

        // Empty-body POST. PostAsync(url, null) sends no Content-Type/Content-Length;
        // the endpoint binding has no [FromBody] parameter so the framework binds the
        // path id, ignores the absent body, and dispatches.
        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        body!.Lifecycle.Should().Be(Lifecycle.Decommissioned);
        // The stored sunsetDate is preserved on Decommission — only the lifecycle column flips.
        body.SunsetDate.Should().NotBeNull();
    }

    [Fact]
    public async Task POST_decommission_when_Deprecated_and_before_sunsetDate_returns_409_with_reason_before_sunset_date()
    {
        var client = await _fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "decommission-app-2", "Decommission App 2", "Desc.");

        // Deprecate with a far-future sunsetDate, then immediately try to decommission
        // — the "now < sunsetDate" branch of Application.Decommission throws
        // InvalidLifecycleTransitionException(reason="before-sunset-date") which the
        // shared handler maps to 409 with both `reason` and `sunsetDate` extensions.
        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        deprecateResp.IsSuccessStatusCode.Should().BeTrue();

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.LifecycleConflict);
        problem.Reason.Should().Be("before-sunset-date");
        problem.CurrentLifecycle.Should().Be("deprecated");
        problem.AttemptedTransition.Should().Be("Decommission");
        // SunsetDate extension carries the originally-stored value so the client knows
        // when the transition would become valid. Round-trip JSON precision tolerance
        // mirrors DeprecateApplicationTests.
        problem.SunsetDate.Should().NotBeNull();
        problem.SunsetDate!.Value.Should().BeCloseTo(sunset, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task POST_decommission_when_Active_returns_409()
    {
        var client = await _fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "decommission-app-3", "Decommission App 3", "Desc.");

        // Skip the deprecate step — the freshly-registered app is Active. The
        // "current state must be Deprecated" invariant rejects with no reason
        // attached (no sunsetDate either, since the entity has none).
        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.LifecycleConflict);
        problem.CurrentLifecycle.Should().Be("active");
        problem.AttemptedTransition.Should().Be("Decommission");
        problem.Reason.Should().BeNull();
    }

    [Fact]
    public async Task POST_decommission_when_already_Decommissioned_returns_409()
    {
        var client = await _fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "decommission-app-4", "Decommission App 4", "Desc.");

        // Drive through Active → Deprecated → Decommissioned, then try one more
        // decommission to land on the wrong-source-state branch.
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        deprecateResp.IsSuccessStatusCode.Should().BeTrue();

        await Task.Delay(2000);
        var firstDecommission = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);
        firstDecommission.IsSuccessStatusCode.Should().BeTrue();

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.LifecycleConflict);
        problem.CurrentLifecycle.Should().Be("decommissioned");
        problem.AttemptedTransition.Should().Be("Decommission");
    }

    [Fact]
    public async Task POST_decommission_for_other_tenants_id_returns_404()
    {
        var orgAClient = await _fx.CreateAuthenticatedClientAsync(OrgAUser);
        var orgARegistered = await RegisterAsync(orgAClient, "decommission-app-5", "App", "Desc.");
        // Deprecate as OrgA so the row is in the right state for the (different) tenant
        // to attempt decommission. RLS hides the row from OrgB so the handler returns
        // null → 404, identical to "no such row".
        var deprecateResp = await orgAClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{orgARegistered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        deprecateResp.IsSuccessStatusCode.Should().BeTrue();

        var orgBClient = await _fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await orgBClient.PostAsync(
            $"/api/v1/catalog/applications/{orgARegistered.Id}/decommission",
            content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<ApplicationResponse> RegisterAsync(HttpClient client, string name, string displayName, string description)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));
        resp.IsSuccessStatusCode.Should().BeTrue();
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
