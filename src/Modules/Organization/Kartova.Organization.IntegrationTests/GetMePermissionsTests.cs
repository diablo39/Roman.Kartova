using System.Net;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public sealed class GetMePermissionsTests : OrganizationIntegrationTestBase
{
    private const string EmailOrgA = "admin@orga.kartova.local";

    [TestMethod]
    public async Task GET_me_permissions_returns_OrgAdmin_set()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(EmailOrgA, new[] { KartovaRoles.OrgAdmin });
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MePermissionsResponse>();

        Assert.IsNotNull(body);
        Assert.AreEqual(KartovaRoles.OrgAdmin, body!.Role);
        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin).ToList(),
            body.Permissions.ToList());
    }

    [TestMethod]
    public async Task GET_me_permissions_returns_Member_set()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(EmailOrgA, new[] { KartovaRoles.Member });
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MePermissionsResponse>();
        Assert.IsNotNull(body);
        Assert.AreEqual(KartovaRoles.Member, body!.Role);
        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.Member).ToList(),
            body.Permissions.ToList());
    }

    [TestMethod]
    public async Task GET_me_permissions_returns_Viewer_set()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(EmailOrgA, new[] { KartovaRoles.Viewer });
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MePermissionsResponse>();
        Assert.IsNotNull(body);
        Assert.AreEqual(KartovaRoles.Viewer, body!.Role);
        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.Viewer).ToList(),
            body.Permissions.ToList());
    }

    [TestMethod]
    public async Task GET_me_permissions_returns_TeamAdmin_set()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(EmailOrgA, new[] { KartovaRoles.TeamAdmin });
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MePermissionsResponse>();
        Assert.IsNotNull(body);
        Assert.AreEqual(KartovaRoles.TeamAdmin, body!.Role);
        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.TeamAdmin).ToList(),
            body.Permissions.ToList());
    }

    [TestMethod]
    public async Task GET_me_permissions_returns_401_when_unauthenticated()
    {
        var client = Fx.CreateAnonymousClient();
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_me_permissions_returns_null_role_when_principal_has_no_realm_role()
    {
        // Issue token with empty roles array — principal authenticates but no ClaimTypes.Role claim
        // is added by TenantClaimsTransformation (roles.Count == 0 skips the flattening loop).
        var client = await Fx.CreateAuthenticatedClientAsync(EmailOrgA, Array.Empty<string>());
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MePermissionsResponse>();
        Assert.IsNotNull(body);
        Assert.IsNull(body!.Role);
        Assert.AreEqual(0, body.Permissions.Count);
    }
}
