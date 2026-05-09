using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class DeprecateApplicationTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    [TestMethod]
    public async Task POST_deprecate_with_future_sunsetDate_returns_200_and_sets_lifecycle_and_sunsetDate()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "deprecate-app-1", "Deprecate App 1", "Desc.");

        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(Lifecycle.Deprecated, body!.Lifecycle);
        // Round-trip through JSON loses sub-microsecond precision; the close-enough
        // tolerance keeps the assertion robust against PostgreSQL's microsecond
        // resolution while still pinning the value end-to-end.
        Assert.IsNotNull(body.SunsetDate);
        var diff = (body.SunsetDate!.Value - sunset).Duration();
        Assert.IsTrue(diff <= TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task POST_deprecate_with_past_sunsetDate_returns_400_with_field_error()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "deprecate-app-2", "Deprecate App 2", "Desc.");

        var past = DateTimeOffset.UtcNow.AddDays(-1);
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(past));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.ValidationFailed, problem!.Type);
        Assert.IsTrue(problem.Errors.ContainsKey("sunsetDate"));
    }

    [TestMethod]
    public async Task POST_deprecate_already_Deprecated_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "deprecate-app-3", "Deprecate App 3", "Desc.");

        // First deprecate succeeds.
        var firstSunset = DateTimeOffset.UtcNow.AddDays(30);
        var first = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(firstSunset));
        Assert.IsTrue(first.IsSuccessStatusCode);

        // Second deprecate on an already-Deprecated row violates the
        // "current state must be Active" invariant → 409 with currentLifecycle.
        var secondSunset = DateTimeOffset.UtcNow.AddDays(60);
        var second = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(secondSunset));

        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
        Assert.AreEqual("deprecated", problem.Extensions["currentLifecycle"]!.ToString());
        Assert.AreEqual("Deprecate", problem.Extensions["attemptedTransition"]!.ToString());
    }

    [TestMethod]
    public async Task POST_deprecate_for_other_tenants_id_returns_404()
    {
        var orgAClient = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var orgARegistered = await RegisterAsync(orgAClient, "deprecate-app-4", "App", "Desc.");

        var orgBClient = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await orgBClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{orgARegistered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_deprecate_without_token_returns_401()
    {
        using var anon = Fx.CreateAnonymousClient();
        var resp = await anon.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{Guid.NewGuid()}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private static async Task<ApplicationResponse> RegisterAsync(HttpClient client, string name, string displayName, string description)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));
        Assert.IsTrue(resp.IsSuccessStatusCode);
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    // Helper for parsing ProblemDetails responses with extensions + validation errors.
    // Mirrors EditApplicationTests.ProblemPayload (which is private nested there too).
    // Acceptable duplication for slice 5 — Task 13 will copy as well; a shared helper
    // could be extracted later if a third caller appears.
    //
    // RFC 7807 envelopes from the API include `errors` (validation) and arbitrary extension
    // members (currentVersion / currentLifecycle / attemptedTransition / ...). System.Text.Json
    // deserialises top-level extension members by NAME against this class's properties — so
    // anything we want to assert on must be declared here. Unknown members are dropped.
    private sealed class ProblemPayload
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Status { get; set; }
        public string? Detail { get; set; }

        // Direct map of the wire `errors` member (camelCase web defaults).
        public Dictionary<string, string[]> Errors { get; set; } = new();

        // Top-level extension members from the RFC 7807 body. We materialise the dictionary
        // surface lazily so test reads (`Extensions["currentLifecycle"]`) match the plan.
        public string? CurrentVersion { get; set; }
        public string? CurrentLifecycle { get; set; }
        public string? AttemptedTransition { get; set; }

        public IDictionary<string, object> Extensions
        {
            get
            {
                var d = new Dictionary<string, object>();
                if (CurrentVersion is not null) d["currentVersion"] = CurrentVersion;
                if (CurrentLifecycle is not null) d["currentLifecycle"] = CurrentLifecycle;
                if (AttemptedTransition is not null) d["attemptedTransition"] = AttemptedTransition;
                return d;
            }
        }
    }
}
