using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class EditApplicationTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    [TestMethod]
    public async Task PUT_with_valid_payload_returns_200_and_advances_version()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-1", "Edit App 1", "Desc 1.");

        var put = NewPut(registered.Id, registered.Version, "Edit App 1 Renamed", "Desc 1 Renamed.");
        var resp = await client.SendAsync(put);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("Edit App 1 Renamed", body!.DisplayName);
        Assert.AreEqual("Desc 1 Renamed.", body.Description);
        Assert.AreNotEqual(registered.Version, body.Version);

        // ETag header must match the new Version field — clients capture this for the
        // next If-Match request.
        Assert.AreEqual($"\"{body.Version}\"", resp.Headers.ETag?.Tag);
    }

    [TestMethod]
    public async Task PUT_without_If_Match_returns_428()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-2", "Edit App 2", "Desc.");

        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/applications/{registered.Id}")
        {
            Content = JsonContent.Create(new EditApplicationRequest("X", "Y")),
        };
        // Intentionally omit If-Match.

        var resp = await client.SendAsync(put);
        Assert.AreEqual(HttpStatusCode.PreconditionRequired, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.PreconditionRequired, problem!.Type);
    }

    [TestMethod]
    public async Task PUT_with_stale_If_Match_returns_412_with_currentVersion()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-3", "Edit App 3", "Desc.");

        // First PUT advances xmin.
        var firstPut = NewPut(registered.Id, registered.Version, "Edit App 3 v2", "Desc v2.");
        var firstResp = await client.SendAsync(firstPut);
        Assert.IsTrue(firstResp.IsSuccessStatusCode);
        var firstBody = (await firstResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;

        // Second PUT uses the original (now stale) version.
        var stalePut = NewPut(registered.Id, registered.Version, "Edit App 3 v3", "Desc v3.");
        var staleResp = await client.SendAsync(stalePut);
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, staleResp.StatusCode);

        var problem = await staleResp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.ConcurrencyConflict, problem!.Type);
        // currentVersion must equal the version returned by the first (successful)
        // PUT — otherwise the client cannot resync without a separate GET, which
        // is the entire reason the extension exists. Asserting the value (not
        // just the key) kills mutations like Encode(0u) and Encode(stale).
        Assert.AreEqual(firstBody.Version, problem.CurrentVersion);
    }

    [TestMethod]
    [DataRow("", "Desc.", "displayName")]
    [DataRow("   ", "Desc.", "displayName")]
    [DataRow("DisplayName", "", "description")]
    [DataRow("DisplayName", "  ", "description")]
    public async Task PUT_with_invalid_field_returns_400_with_field_error(string displayName, string description, string expectedErrorField)
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, $"edit-app-4-{Guid.NewGuid():N}", "Edit App 4", "Desc.");

        var put = NewPut(registered.Id, registered.Version, displayName, description);
        var resp = await client.SendAsync(put);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.ValidationFailed, problem!.Type);
        Assert.IsTrue(problem.Errors.ContainsKey(expectedErrorField));
    }

    [TestMethod]
    public async Task PUT_with_over_length_displayName_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-5", "Edit App 5", "Desc.");

        var put = NewPut(registered.Id, registered.Version, new string('x', 129), "Desc.");
        var resp = await client.SendAsync(put);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.IsTrue(problem!.Errors.ContainsKey("displayName"));
    }

    [TestMethod]
    public async Task PUT_for_other_tenants_id_returns_404()
    {
        var orgAClient = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var orgARegistered = await RegisterAsync(orgAClient, "edit-app-6", "App", "Desc.");

        var orgBClient = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var put = NewPut(orgARegistered.Id, orgARegistered.Version, "Hijack", "Hijack.");
        var resp = await orgBClient.SendAsync(put);

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_without_token_returns_401()
    {
        using var anon = Fx.CreateAnonymousClient();
        var put = NewPut(Guid.NewGuid(), VersionEncoding.Encode(0u), "X", "Y");
        var resp = await anon.SendAsync(put);
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_on_Deprecated_application_returns_200()
    {
        // Spec §9.8 step 5: Deprecated still allows edit. The terminal-write
        // 409 fires only on Decommissioned. Mirrors the domain test
        // ApplicationLifecycleTests.EditMetadata_on_Deprecated_succeeds —
        // pinned at integration tier so a mutation that flipped the guard
        // direction is caught end-to-end.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-deprecated", "App", "Desc.");

        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);
        var deprecated = (await deprecateResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;

        var put = NewPut(deprecated.Id, deprecated.Version, "Renamed While Deprecated", "Updated desc.");
        var resp = await client.SendAsync(put);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
        Assert.AreEqual("Renamed While Deprecated", body.DisplayName);
        Assert.AreEqual(Lifecycle.Deprecated, body.Lifecycle);
    }

    [TestMethod]
    public async Task PUT_on_Decommissioned_application_returns_409()
    {
        // Drives an application through Active → Deprecated → Decommissioned
        // via the lifecycle endpoints, then asserts that PUT returns 409 with
        // type=lifecycle-conflict (the terminal-state guard in
        // Application.EditMetadata).
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-7", "App", "Desc.");

        var deprecate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/catalog/applications/{registered.Id}/deprecate")
        {
            Content = JsonContent.Create(new { sunsetDate = DateTimeOffset.UtcNow.AddSeconds(1) }),
        };
        var deprecateResp = await client.SendAsync(deprecate);
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        // Wait a bit and decommission
        await Task.Delay(2000);
        var decommission = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/catalog/applications/{registered.Id}/decommission");
        var decommissionResp = await client.SendAsync(decommission);
        Assert.IsTrue(decommissionResp.IsSuccessStatusCode);

        var current = await decommissionResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);

        // Now try to edit
        var put = NewPut(registered.Id, current!.Version, "X", "Y");
        var resp = await client.SendAsync(put);

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
        Assert.AreEqual("decommissioned", problem.Extensions["currentLifecycle"]!.ToString());
        Assert.AreEqual("EditMetadata", problem.Extensions["attemptedTransition"]!.ToString());
    }

    private static async Task<ApplicationResponse> RegisterAsync(HttpClient client, string name, string displayName, string description)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));
        Assert.IsTrue(resp.IsSuccessStatusCode);
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    private static HttpRequestMessage NewPut(Guid id, string version, string displayName, string description)
    {
        var msg = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/applications/{id}")
        {
            Content = JsonContent.Create(new EditApplicationRequest(displayName, description)),
        };
        msg.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        return msg;
    }

    // Helper for parsing ProblemDetails responses with extensions + validation errors.
    // RFC 7807 envelopes from the API include `errors` (validation) and arbitrary extension
    // members (currentVersion / currentLifecycle / attemptedTransition / ...). System.Text.Json
    // deserialises top-level extension members by NAME against this class's properties — so
    // anything we want to assert on must be declared here. Unknown members are dropped.
    //
    // The wire format puts extension members at the TOP level of the JSON object (a flat
    // ProblemDetails — not under a nested `extensions` key). The Errors / Extensions
    // properties below project the typed setters onto the dictionary surface the tests
    // already use, so the assertion form remains identical to the plan's reference.
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
