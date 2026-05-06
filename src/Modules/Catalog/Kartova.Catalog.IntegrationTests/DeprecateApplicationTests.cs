using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[Collection(KartovaApiCollection.Name)]
public class DeprecateApplicationTests
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private readonly KartovaApiFixture _fx;

    public DeprecateApplicationTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task POST_deprecate_with_future_sunsetDate_returns_200_and_sets_lifecycle_and_sunsetDate()
    {
        var client = await _fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "deprecate-app-1", "Deprecate App 1", "Desc.");

        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        body!.Lifecycle.Should().Be(Lifecycle.Deprecated);
        // Round-trip through JSON loses sub-microsecond precision; the close-enough
        // tolerance keeps the assertion robust against PostgreSQL's microsecond
        // resolution while still pinning the value end-to-end.
        body.SunsetDate.Should().NotBeNull();
        body.SunsetDate!.Value.Should().BeCloseTo(sunset, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task POST_deprecate_with_past_sunsetDate_returns_400_with_field_error()
    {
        var client = await _fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "deprecate-app-2", "Deprecate App 2", "Desc.");

        var past = DateTimeOffset.UtcNow.AddDays(-1);
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(past));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("sunsetDate");
    }

    [Fact]
    public async Task POST_deprecate_already_Deprecated_returns_409()
    {
        var client = await _fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "deprecate-app-3", "Deprecate App 3", "Desc.");

        // First deprecate succeeds.
        var firstSunset = DateTimeOffset.UtcNow.AddDays(30);
        var first = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(firstSunset));
        first.IsSuccessStatusCode.Should().BeTrue();

        // Second deprecate on an already-Deprecated row violates the
        // "current state must be Active" invariant → 409 with currentLifecycle.
        var secondSunset = DateTimeOffset.UtcNow.AddDays(60);
        var second = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(secondSunset));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.LifecycleConflict);
        problem.Extensions["currentLifecycle"]!.ToString().Should().Be("Deprecated");
        problem.Extensions["attemptedTransition"]!.ToString().Should().Be("Deprecate");
    }

    [Fact]
    public async Task POST_deprecate_for_other_tenants_id_returns_404()
    {
        var orgAClient = await _fx.CreateAuthenticatedClientAsync(OrgAUser);
        var orgARegistered = await RegisterAsync(orgAClient, "deprecate-app-4", "App", "Desc.");

        var orgBClient = await _fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await orgBClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{orgARegistered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_deprecate_without_token_returns_401()
    {
        using var anon = _fx.CreateAnonymousClient();
        var resp = await anon.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{Guid.NewGuid()}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<ApplicationResponse> RegisterAsync(HttpClient client, string name, string displayName, string description)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));
        resp.IsSuccessStatusCode.Should().BeTrue();
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
