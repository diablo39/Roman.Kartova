using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
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
        var registered = await RegisterAsync(client, "Decommission App 1", "Desc.");

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
    public async Task POST_decommission_with_overrideSunset_true_as_OrgAdmin_before_sunsetDate_returns_200_and_sets_lifecycle_to_Decommissioned()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "Decommission App Override 1", "Desc.");

        // Deprecate with a far-future sunsetDate so the "before sunset" guard would
        // normally reject Decommission with 409 — the override bypasses it.
        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            new DecommissionApplicationRequest(OverrideSunset: true));

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(Lifecycle.Decommissioned, body!.Lifecycle);
    }

    [TestMethod]
    public async Task POST_decommission_override_before_sunset_writes_lifecycle_changed_audit_with_override_keys()
    {
        // ADR-0073's justification for the override is that the bypass is logged. Prove
        // the handler COMPUTES bypassed (not just that the factory can emit the keys):
        // an OrgAdmin override before the far-future sunset must write overrodeSunset
        // + bypassedSunsetDate onto the Decommission lifecycle_changed row.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "Decommission Override Audit", "Desc.");

        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            new DecommissionApplicationRequest(OverrideSunset: true));
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var decommissionRow = await ReadDecommissionAuditRowAsync(registered.Id);
        using var data = JsonDocument.Parse(decommissionRow.DataJson!);
        Assert.AreEqual("true", data.RootElement.GetProperty("overrodeSunset").GetString());
        var bypassed = DateTimeOffset.Parse(data.RootElement.GetProperty("bypassedSunsetDate").GetString()!);
        Assert.IsTrue((bypassed - sunset).Duration() <= TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task POST_decommission_override_true_after_sunset_omits_override_audit_keys()
    {
        // The override flag being true is not sufficient — the handler only records the
        // bypass when it ACTUALLY bypassed (now < sunset). After the sunset has elapsed
        // the transition is legal on its own, so bypassed=false and the override keys
        // must be absent. Kills the `now < sunset` mutation on the bypass computation.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "Decommission Override Audit After", "Desc.");

        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        await Task.Delay(2000);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            new DecommissionApplicationRequest(OverrideSunset: true));
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var decommissionRow = await ReadDecommissionAuditRowAsync(registered.Id);
        using var data = JsonDocument.Parse(decommissionRow.DataJson!);
        Assert.IsFalse(data.RootElement.TryGetProperty("overrodeSunset", out _));
        Assert.IsFalse(data.RootElement.TryGetProperty("bypassedSunsetDate", out _));
    }

    // Returns the single lifecycle_changed row for the Deprecated→Decommissioned
    // transition (the Active→Deprecated row shares the action, so filter on to=Decommissioned).
    private async Task<KartovaApiFixture.AuditRowRecord> ReadDecommissionAuditRowAsync(Guid targetId)
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var rows = await Fx.ReadAuditLogAsync(tenantId.Value);
        return rows.Single(r =>
        {
            if (r.Action != CatalogAuditActions.ApplicationLifecycleChanged
                || r.TargetId != targetId.ToString())
            {
                return false;
            }
            using var d = JsonDocument.Parse(r.DataJson!);
            return d.RootElement.GetProperty("to").GetString() == nameof(Lifecycle.Decommissioned);
        });
    }

    [TestMethod]
    public async Task POST_decommission_with_overrideSunset_true_as_Member_before_sunsetDate_returns_403()
    {
        // Member must belong to the app's owning team to clear the prior
        // ApplicationTeamScoped resource gate — otherwise a 403 there would be a false
        // positive for this test's actual target (the override-permission check).
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "Override Team 403");
        var registerResp = await admin.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Decommission App Override 2", "Desc.", teamId));
        Assert.IsTrue(registerResp.IsSuccessStatusCode);
        var registered = (await registerResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;

        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var deprecateResp = await admin.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var member = await Fx.CreateAuthenticatedClientAsync("member-override-403@orga.kartova.local", new[] { KartovaRoles.Member });
        var memberId = await Fx.GetSubClaimAsync("member-override-403@orga.kartova.local");
        await Fx.SeedTeamMembershipAsync(teamId, memberId, roleByte: 1 /* Member */);

        // Member has catalog.applications.lifecycle.forward but not the OrgAdmin-only
        // catalog.applications.lifecycle.override permission — AuthorizeAsync fails
        // and the delegate returns Forbid() before the command is dispatched.
        var resp = await member.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            new DecommissionApplicationRequest(OverrideSunset: true));

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_decommission_when_Deprecated_and_before_sunsetDate_returns_409_with_reason_before_sunset_date()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "Decommission App 2", "Desc.");

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
    public async Task POST_decommission_as_Member_without_override_before_sunsetDate_returns_409_with_reason_before_sunset_date()
    {
        // Member must belong to the app's owning team to clear the prior
        // ApplicationTeamScoped resource gate (same as the override-403 test above).
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "Override Team 409");
        var registerResp = await admin.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Decommission App 2b", "Desc.", teamId));
        Assert.IsTrue(registerResp.IsSuccessStatusCode);
        var registered = (await registerResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;

        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var deprecateResp = await admin.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        var member = await Fx.CreateAuthenticatedClientAsync("member-nooverride-409@orga.kartova.local", new[] { KartovaRoles.Member });
        var memberId = await Fx.GetSubClaimAsync("member-nooverride-409@orga.kartova.local");
        await Fx.SeedTeamMembershipAsync(teamId, memberId, roleByte: 1 /* Member */);

        // Regression: an empty body / overrideSunset=false request behaves exactly
        // like the pre-override endpoint — 409 before-sunset-date, no auth bypass
        // is attempted since the override branch is only entered when the flag is true.
        var resp = await member.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            new DecommissionApplicationRequest(OverrideSunset: false));

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
        Assert.AreEqual("before-sunset-date", problem.Reason);
    }

    [TestMethod]
    public async Task POST_decommission_as_OrgAdmin_without_override_after_sunsetDate_returns_200()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "Decommission App 2c", "Desc.");

        // Same past-sunset crossing pattern as the first test in this file — regression
        // check that the ordinary (no override) path is unaffected by the new code.
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecateResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.IsTrue(deprecateResp.IsSuccessStatusCode);

        await Task.Delay(2000);

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission",
            content: null);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(Lifecycle.Decommissioned, body!.Lifecycle);
    }

    [TestMethod]
    public async Task POST_decommission_when_Active_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var registered = await RegisterAsync(client, "Decommission App 3", "Desc.");

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
        var registered = await RegisterAsync(client, "Decommission App 4", "Desc.");

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
        var orgARegistered = await RegisterAsync(orgAClient, "App", "Desc.");
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

    // ADR-0103: register requires a valid owning team in the tenant. All callers
    // register as OrgA, so seed the team in OrgA's tenant (BYPASSRLS, idempotent).
    private async Task<ApplicationResponse> RegisterAsync(HttpClient client, string displayName, string description)
    {
        var teamId = await Fx.SeedTeamInOrganizationAsync(
            Fx.TenantIdForEmail("admin@orga.kartova.local"), "Lifecycle Team");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(displayName, description, teamId));
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
