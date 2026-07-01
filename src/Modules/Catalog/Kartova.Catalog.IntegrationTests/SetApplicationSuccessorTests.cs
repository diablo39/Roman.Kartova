using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Real-seam tests for PUT /applications/{id}/successor (ADR-0110 §5.3). Mirrors
/// <see cref="DeprecateApplicationTests"/>'s structure and helpers. PUT is
/// idempotent replacement (ADR-0096) — a null body clears the successor.
///
/// Existence pre-check (delegate) → 422 invalid-successor for an unknown or
/// cross-tenant id. Domain guards (<c>Application.SetSuccessor</c>) own 409 for a
/// not-Deprecated source and 400 for a self-successor id — this file does not
/// duplicate either guard, only asserts the resulting envelope.
/// </summary>
[TestClass]
public sealed class SetApplicationSuccessorTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    [TestMethod]
    public async Task PUT_successor_on_Deprecated_app_with_valid_same_tenant_successor_returns_200_and_sets_it()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var target = await RegisterAsync(client, "Successor Target 1", "Desc.");
        var successor = await RegisterAsync(client, "Successor App 1", "Desc.");

        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/successor",
            new SetApplicationSuccessorRequest(successor.Id));

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(successor.Id, body!.SuccessorApplicationId);
    }

    [TestMethod]
    public async Task PUT_successor_on_Deprecated_app_with_existing_successor_to_null_returns_200_and_clears_it()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var target = await RegisterAsync(client, "Successor Target 2", "Desc.");
        var successor = await RegisterAsync(client, "Successor App 2", "Desc.");

        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var setResp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/successor",
            new SetApplicationSuccessorRequest(successor.Id));
        Assert.IsTrue(setResp.IsSuccessStatusCode);

        var clearResp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/successor",
            new SetApplicationSuccessorRequest(null));

        Assert.AreEqual(HttpStatusCode.OK, clearResp.StatusCode);
        var body = await clearResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNull(body!.SuccessorApplicationId);
    }

    [TestMethod]
    public async Task PUT_successor_on_Active_app_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var target = await RegisterAsync(client, "Successor Target 3", "Desc.");
        var successor = await RegisterAsync(client, "Successor App 3", "Desc.");

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/successor",
            new SetApplicationSuccessorRequest(successor.Id));

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
        Assert.AreEqual("active", problem.CurrentLifecycle);
    }

    [TestMethod]
    public async Task PUT_successor_on_Decommissioned_app_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var target = await RegisterAsync(client, "Successor Target 4", "Desc.");
        var successor = await RegisterAsync(client, "Successor App 4", "Desc.");

        // Drive Active → Deprecated → Decommissioned via the far-future-sunset +
        // OrgAdmin overrideSunset path (DecommissionApplicationTests' fast route —
        // avoids a real-time Task.Delay past a near sunsetDate).
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var decommissionResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/decommission",
            new DecommissionApplicationRequest(OverrideSunset: true));
        Assert.IsTrue(decommissionResp.IsSuccessStatusCode);

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/successor",
            new SetApplicationSuccessorRequest(successor.Id));

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
        Assert.AreEqual("decommissioned", problem.CurrentLifecycle);
    }

    [TestMethod]
    public async Task PUT_successor_on_Deprecated_app_with_unknown_successor_id_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var target = await RegisterAsync(client, "Successor Target 5", "Desc.");

        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/successor",
            new SetApplicationSuccessorRequest(Guid.NewGuid()));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.InvalidSuccessor, problem!.Type);
    }

    [TestMethod]
    public async Task PUT_successor_on_Deprecated_app_with_self_id_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var target = await RegisterAsync(client, "Successor Target 6", "Desc.");

        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/successor",
            new SetApplicationSuccessorRequest(target.Id));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_successor_as_non_member_of_the_apps_team_returns_403()
    {
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var appTeamId = await Fx.SeedTeamInOrganizationAsync(tenant, "Successor 403 App Team");
        var registerResp = await admin.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Successor Target 7", "Desc.", appTeamId));
        Assert.IsTrue(registerResp.IsSuccessStatusCode);
        var target = (await registerResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
        var successor = await RegisterAsync(admin, "Successor App 7", "Desc.");

        var deprecateResp = await admin.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        // A Member seeded onto a DIFFERENT team in the SAME tenant: visible under RLS
        // (so LoadAndAuthorizeApplicationAsync's existence check passes, ruling out
        // 404), but fails ApplicationTeamScoped (not OrgAdmin, not a member of the
        // app's own team "Successor 403 App Team") — Forbid() before the command
        // dispatches. Mirrors DecommissionApplicationTests' override-403 test's
        // seeding mechanics, but the member team is deliberately NOT the app's team
        // (that test does the opposite, to clear this same gate before testing a
        // different, deeper one).
        var otherTeamId = await Fx.SeedTeamInOrganizationAsync(tenant, "Successor 403 Other Team");
        const string memberEmail = "member-successor-403@orga.kartova.local";
        var member = await Fx.CreateAuthenticatedClientAsync(memberEmail, new[] { KartovaRoles.Member });
        var memberId = await Fx.GetSubClaimAsync(memberEmail);
        await Fx.SeedTeamMembershipAsync(otherTeamId, memberId, roleByte: 1 /* Member */);

        var resp = await member.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{target.Id}/successor",
            new SetApplicationSuccessorRequest(successor.Id));

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ADR-0103: register requires a valid owning team in the tenant. All callers
    // register as OrgA, so seed the team in OrgA's tenant (BYPASSRLS, idempotent).
    private async Task<ApplicationResponse> RegisterAsync(HttpClient client, string displayName, string description)
    {
        var teamId = await Fx.SeedTeamInOrganizationAsync(
            Fx.TenantIdForEmail(OrgAUser), "Lifecycle Team");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(displayName, description, teamId));
        Assert.IsTrue(resp.IsSuccessStatusCode);
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    // Helper for parsing ProblemDetails responses — mirrors DeprecateApplicationTests'
    // identical nested class (itself mirroring EditApplicationTests). Acceptable
    // duplication per that file's existing comment; a shared helper could be
    // extracted later if desired.
    private sealed class ProblemPayload
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Status { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]> Errors { get; set; } = new();
        public string? CurrentVersion { get; set; }
        public string? CurrentLifecycle { get; set; }
        public string? AttemptedTransition { get; set; }
    }
}
