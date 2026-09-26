using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// ADR-0118 isolation matrix on the real host: each realm's tokens are rejected on the other
/// realm's routes by scheme, independent of role claims. 401s are multi-cause, so they assert
/// the WWW-Authenticate discriminator (token rejected vs no token), not the status alone.
/// </summary>
[TestClass]
public class AdminSchemeIsolationTests : OrganizationIntegrationTestBase
{
    private const string SessionMe = "/api/v1/admin/session/me";

    private HttpClient ClientWith(string token)
    {
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task Platform_token_gets_200_with_operator_identity()
    {
        var sub = Guid.NewGuid();
        var client = ClientWith(Fx.Signer.IssueForPlatformRealm(
            new[] { KartovaRoles.PlatformAdmin }, subject: sub.ToString(), email: "op@kartova.local", name: "Op Erator"));

        var resp = await client.GetAsync(SessionMe);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AdminMeResponse>();
        Assert.AreEqual(new AdminMeResponse(sub, "op@kartova.local", "Op Erator"), body);
    }

    [TestMethod]
    public async Task Tenant_token_claiming_platform_admin_gets_401_on_admin_session()
    {
        // GUID subject (not the default "test-user"): if scheme isolation regresses, the request
        // reaches GetMe and — with a parseable sub — returns 200, so the status assertion below
        // fails on its own instead of the regression hiding behind GetMe's invalid-sub 401 branch.
        var client = ClientWith(Fx.Signer.IssueForTenant(
            SeededOrgs.OrgA, new[] { KartovaRoles.PlatformAdmin }, subject: Guid.NewGuid().ToString()));

        var resp = await client.GetAsync(SessionMe);

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token");
    }

    [TestMethod]
    public async Task Platform_token_gets_401_on_tenant_route()
    {
        var client = ClientWith(Fx.Signer.IssueForPlatformAdmin());

        var resp = await client.GetAsync("/api/v1/organizations/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token");
    }

    [TestMethod]
    public async Task Platform_token_without_role_gets_403()
    {
        var client = ClientWith(Fx.Signer.IssueForPlatformRealm(Array.Empty<string>()));

        var resp = await client.GetAsync(SessionMe);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Anonymous_gets_401_without_token_error()
    {
        var resp = await Fx.CreateAnonymousClient().GetAsync(SessionMe);

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.IsFalse(resp.Headers.WwwAuthenticate.ToString().Contains("invalid_token"),
            "anonymous 401 is the no-token branch; invalid_token would mean a token was presented.");
    }

    /// <summary>ADR-0118 spec item: /health/detailed shares the PlatformAdminOnly gate — pin it here too.</summary>
    [TestMethod]
    public async Task Tenant_token_claiming_platform_admin_gets_401_on_health_detailed()
    {
        var client = ClientWith(Fx.Signer.IssueForTenant(
            SeededOrgs.OrgA, new[] { KartovaRoles.PlatformAdmin }, subject: Guid.NewGuid().ToString()));

        var resp = await client.GetAsync("/health/detailed");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token");
    }

    [TestMethod]
    public async Task Platform_token_without_role_gets_403_on_health_detailed()
    {
        var client = ClientWith(Fx.Signer.IssueForPlatformRealm(Array.Empty<string>()));

        var resp = await client.GetAsync("/health/detailed");

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    /// <summary>
    /// A platform-realm-shaped token signed by a DIFFERENT key (not the fixture's shared
    /// <see cref="TestJwtSigner"/>) must be rejected by signature, independent of issuer/audience
    /// string matches — the fixture-level analogue of the live-KeyCloak forged-key test.
    /// </summary>
    [TestMethod]
    public async Task Platform_token_signed_by_a_foreign_key_gets_401()
    {
        var client = ClientWith(new TestJwtSigner().IssueForPlatformAdmin(subject: Guid.NewGuid().ToString()));

        var resp = await client.GetAsync(SessionMe);

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token");
    }
}
