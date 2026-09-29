using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Kartova.Organization.Contracts;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Kartova.Api.IntegrationTests;

/// <summary>
/// ADR-0118 against real KeyCloak: tokens minted by the kartova-platform realm (its own issuer,
/// signing keys and audience mapper) are accepted on the operator surface, and the two realms'
/// tokens do not cross over.
/// </summary>
[TestClass]
public class PlatformRealmLiveTokenTests : KeycloakContainerTestBase
{
    private WebApplicationFactory<Program>? _app;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        try
        {
            await PostgresTestBootstrap.SeedRolesAndSchemaAsync(Containers.Postgres.GetConnectionString());
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateObject)
        {
            // Seeded by another test class sharing this assembly's Postgres container.
        }
        await PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.MigratorRole),
            o => new OrganizationDbContext(o));

        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.AppRole));
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole));
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), Containers.KeycloakAuthority);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.MetadataAddress),
            $"{Containers.KeycloakAuthority}/.well-known/openid-configuration");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), "kartova-api");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");
        SetPlatformAdminAuthEnv();
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__BaseUrl", Containers.KeycloakBaseUrl);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__Realm", RealmSeedConstants.RealmName);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientId", RealmSeedConstants.AdminClientId);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientSecret", RealmSeedConstants.AdminClientSecret);

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
    }

    [TestCleanup]
    public void DisposeAsync() => _app?.Dispose();

    private HttpClient ClientWith(string token)
    {
        var client = _app!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task Real_platform_realm_token_reaches_admin_session_me()
    {
        var token = await GetRealPlatformTokenAsync("platform-admin@kartova.local", "dev_password_12");

        var resp = await ClientWith(token).GetAsync("/api/v1/admin/session/me");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, await resp.Content.ReadAsStringAsync());
        var me = await resp.Content.ReadFromJsonAsync<AdminMeResponse>();
        Assert.AreEqual("platform-admin@kartova.local", me!.Email);
        Assert.AreEqual("Platform Admin", me.DisplayName);
        Assert.AreNotEqual(Guid.Empty, me.UserId);
    }

    [TestMethod]
    public async Task Real_tenant_realm_OrgAdmin_token_is_rejected_on_admin_session_me()
    {
        var token = await GetRealTokenAsync("admin@orga.kartova.local", "dev_password_12");

        var resp = await ClientWith(token).GetAsync("/api/v1/admin/session/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token");
    }

    [TestMethod]
    public async Task Real_platform_realm_token_is_rejected_on_tenant_route()
    {
        var token = await GetRealPlatformTokenAsync("platform-admin@kartova.local", "dev_password_12");

        var resp = await ClientWith(token).GetAsync("/api/v1/organizations/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token");
    }

    /// <summary>
    /// ADR-0118 gate 7 fix: a forged token that copies the real platform realm's <c>iss</c> and
    /// stamps the right <c>aud</c> and role claim, but is signed with an attacker-controlled key,
    /// must still be rejected. This is the discriminating case that proves the live host validates
    /// signatures against the realm's JWKS rather than trusting issuer/audience string matches alone.
    /// </summary>
    [TestMethod]
    public async Task Forged_token_with_platform_issuer_and_audience_but_wrong_signing_key_is_rejected()
    {
        var realToken = await GetRealPlatformTokenAsync("platform-admin@kartova.local", "dev_password_12");
        var issuer = new JwtSecurityTokenHandler().ReadJwtToken(realToken).Issuer;

        using var forgedRsa = RSA.Create(2048);
        var forgedKey = new RsaSecurityKey(forgedRsa) { KeyId = "forged" };

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new("realm_access", JsonSerializer.Serialize(new { roles = new[] { "platform-admin" } }), JsonClaimValueTypes.Json),
        };

        var now = DateTime.UtcNow;
        var forgedToken = new JwtSecurityToken(
            issuer: issuer,
            audience: "kartova-admin-api",
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(forgedKey, SecurityAlgorithms.RsaSha256));

        var token = new JwtSecurityTokenHandler().WriteToken(forgedToken);

        var resp = await ClientWith(token).GetAsync("/api/v1/admin/session/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token");
    }

    [TestMethod]
    public async Task Real_platform_realm_token_without_the_role_gets_403_on_admin_session_me()
    {
        var token = await GetRealPlatformTokenAsync("operator-norole@kartova.local", "dev_password_12");

        var resp = await ClientWith(token).GetAsync("/api/v1/admin/session/me");

        // 403 has a single cause here (authenticated by the PlatformAdmin scheme, role missing), so status is discriminative.
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }
}
