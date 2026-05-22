using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class ReactivateApplicationTests : CatalogIntegrationTestBase
{
    private const string OrgAdminEmail = "admin@orga.kartova.local";
    private const string MemberEmail   = "member@orga.kartova.local";

    [TestMethod]
    public async Task POST_reactivate_from_Deprecated_returns_200_with_Active_state_and_no_sunsetDate()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registered = await RegisterAsync(client, "react-app-1", "Reactivate 1", "Desc.");

        // Deprecate first.
        var dep = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });
        Assert.IsTrue(dep.IsSuccessStatusCode);

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/reactivate", content: null);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(Lifecycle.Active, body!.Lifecycle);
        Assert.IsNull(body.SunsetDate);
    }

    [TestMethod]
    public async Task POST_reactivate_from_Active_returns_409_lifecycle_conflict()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registered = await RegisterAsync(client, "react-app-2", "Reactivate 2", "Desc.");

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/reactivate", content: null);

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
    }

    [TestMethod]
    public async Task POST_reactivate_as_Member_returns_403()
    {
        var orgAdminClient = await Fx.CreateAuthenticatedClientAsync(OrgAdminEmail, new[] { KartovaRoles.OrgAdmin });
        var registered = await RegisterAsync(orgAdminClient, "react-app-3", "Reactivate 3", "Desc.");

        await orgAdminClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });

        var memberClient = await Fx.CreateAuthenticatedClientAsync(MemberEmail, new[] { KartovaRoles.Member });
        var resp = await memberClient.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/reactivate", content: null);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_reactivate_unauthenticated_returns_401()
    {
        var client = Fx.CreateAnonymousClient();
        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{Guid.NewGuid()}/reactivate", content: null);

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

    // Mirror of ProblemPayload in DeprecateApplicationTests / DecommissionApplicationTests.
    // Reactivate only needs Type for the 409 assertion; retaining the full shape for
    // consistency with the other lifecycle test classes.
    private sealed class ProblemPayload
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Status { get; set; }
        public string? Detail { get; set; }

        public Dictionary<string, string[]> Errors { get; set; } = new();

        public string? CurrentLifecycle { get; set; }
        public string? AttemptedTransition { get; set; }
    }
}
