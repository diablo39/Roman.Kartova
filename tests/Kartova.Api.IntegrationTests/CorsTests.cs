using System.Net;
using System.Net.Http.Headers;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kartova.Api.IntegrationTests;

[TestClass]
public class CorsTests : KeycloakContainerTestBase
{
    private const string TenantOrigin = "http://localhost:5173";
    private const string AdminOrigin = "http://localhost:5174";
    private const string AdminRoute = "/api/v1/admin/session/me";
    private const string TenantRoute = "/api/v1/organizations/me";

    private WebApplicationFactory<Program>? _app;

    [TestInitialize]
    public void InitializeAsync()
    {
        // Env vars must be set BEFORE the WebApplicationFactory boots the host.
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

        // Slice 9 / H8: AddKeycloakAdminClient.ValidateOnStart rejects the
        // appsettings placeholder "OVERRIDE_VIA_ENV". Wire the four
        // KartovaIdentity__Keycloak__* env vars from the live container so the
        // host boots. Realm-seed literals live in RealmSeedConstants so a
        // future rename of kartova-realm.json only touches one site.
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__BaseUrl", Containers.KeycloakBaseUrl);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__Realm", RealmSeedConstants.RealmName);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientId", RealmSeedConstants.AdminClientId);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientSecret", RealmSeedConstants.AdminClientSecret);

        // CORS allowlist — set before WAF boots so the policy builder sees the value.
        Environment.SetEnvironmentVariable($"{CorsConfigKeys.AllowedOrigins.Replace(":", "__")}__0", "http://localhost:5173");
        // Admin allowlist (ADR-0118) — deliberately slash-suffixed: Program.cs must normalize it so the
        // browser's slash-less Origin header still matches (Review Focus #1).
        Environment.SetEnvironmentVariable($"{CorsConfigKeys.AdminAllowedOrigins.Replace(":", "__")}__0", AdminOrigin + "/");

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
        });
    }

    [TestCleanup]
    public void DisposeAsync()
    {
        _app?.Dispose();
    }

    [TestMethod]
    public async Task Preflight_FromConfiguredOrigin_AllowsRequest()
    {
        var client = _app!.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/version");
        req.Headers.Add("Origin", "http://localhost:5173");
        req.Headers.Add("Access-Control-Request-Method", "GET");

        var resp = await client.SendAsync(req);

        Assert.IsTrue(resp.Headers.Contains("Access-Control-Allow-Origin"));
        var origins = resp.Headers.GetValues("Access-Control-Allow-Origin").ToList();
        Assert.AreEqual(1, origins.Count);
        Assert.AreEqual("http://localhost:5173", origins[0]);
    }

    [TestMethod]
    public async Task Preflight_FromUnknownOrigin_DoesNotEchoOrigin()
    {
        var client = _app!.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/version");
        req.Headers.Add("Origin", "https://evil.example");
        req.Headers.Add("Access-Control-Request-Method", "GET");

        var resp = await client.SendAsync(req);

        Assert.IsFalse(
            resp.Headers.Contains("Access-Control-Allow-Origin"),
            "the API must not echo origins outside the configured allowlist.");
    }

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, path);
        req.Headers.Add("Origin", origin);
        req.Headers.Add("Access-Control-Request-Method", "GET");
        req.Headers.Add("Access-Control-Request-Headers", "authorization");
        return req;
    }

    private static string? AllowOrigin(HttpResponseMessage resp) =>
        resp.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) ? v.Single() : null;

    [TestMethod]
    public async Task Preflight_from_unknown_origin_to_admin_route_does_not_echo_origin()
    {
        var resp = await _app!.CreateClient().SendAsync(Preflight(AdminRoute, "https://evil.example"));

        Assert.IsNull(AllowOrigin(resp), "an origin outside both allowlists must not be able to read admin responses.");
    }

    [TestMethod]
    public async Task Preflight_from_admin_origin_to_admin_route_allows_admin_origin()
    {
        var resp = await _app!.CreateClient().SendAsync(Preflight(AdminRoute, AdminOrigin));

        Assert.AreEqual(AdminOrigin, AllowOrigin(resp));
    }

    [TestMethod]
    public async Task Preflight_from_tenant_origin_to_admin_route_does_not_echo_origin()
    {
        var resp = await _app!.CreateClient().SendAsync(Preflight(AdminRoute, TenantOrigin));

        Assert.IsNull(AllowOrigin(resp), "the tenant origin must not be able to read admin responses (ADR-0118).");
    }

    [TestMethod]
    public async Task Preflight_from_admin_origin_to_tenant_route_does_not_echo_origin()
    {
        var resp = await _app!.CreateClient().SendAsync(Preflight(TenantRoute, AdminOrigin));

        Assert.IsNull(AllowOrigin(resp), "the operator origin must not be able to read tenant responses (ADR-0118).");
    }

    [TestMethod]
    public async Task Get_from_admin_origin_with_live_operator_token_returns_200_with_admin_origin()
    {
        var token = await GetRealPlatformTokenAsync("platform-admin@kartova.local", "dev_password_12");
        var req = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        req.Headers.Add("Origin", AdminOrigin);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _app!.CreateClient().SendAsync(req);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, await resp.Content.ReadAsStringAsync());
        Assert.AreEqual(AdminOrigin, AllowOrigin(resp));
    }

    [TestMethod]
    public async Task Get_from_tenant_origin_to_admin_route_does_not_echo_origin()
    {
        var token = await GetRealPlatformTokenAsync("platform-admin@kartova.local", "dev_password_12");
        var req = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        req.Headers.Add("Origin", TenantOrigin);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _app!.CreateClient().SendAsync(req);

        // The API may still answer 200 (CORS is enforced by the browser); the missing ACAO is what blocks the read.
        Assert.IsNull(AllowOrigin(resp), "the tenant origin must not be able to read admin responses (ADR-0118).");
    }

    /// <summary>
    /// M5 (gate-6 finding): nothing pinned that Program.cs actually calls CorsOriginLists.Validate — a
    /// regression there would only surface as a security bug in a running system, not a red test. Boots a
    /// SEPARATE factory with an overlapping allow-list and asserts the host refuses to start.
    /// Cors__AdminAllowedOrigins__0 is process-wide env, shared with every other test in this class (which
    /// expect it to be the disjoint AdminOrigin) — override it only for this test and restore it in `finally`.
    /// </summary>
    [TestMethod]
    public async Task Startup_throws_when_AdminAllowedOrigins_overlaps_AllowedOrigins()
    {
        var adminOriginsKey = $"{CorsConfigKeys.AdminAllowedOrigins.Replace(":", "__")}__0";
        var previous = Environment.GetEnvironmentVariable(adminOriginsKey);
        try
        {
            Environment.SetEnvironmentVariable(adminOriginsKey, TenantOrigin); // overlap: same as Cors:AllowedOrigins:0
            using var overlapApp = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));

            Exception? thrown = null;
            try
            {
                using var client = overlapApp.CreateClient();
                await client.GetAsync("/api/v1/version");
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.IsNotNull(thrown, "Expected the host to fail to start because the two CORS allow-lists overlap.");
            var invalidOperation = InnermostInvalidOperationException(thrown)
                ?? throw new AssertFailedException(
                    $"Expected an InvalidOperationException somewhere in the exception chain; got: {thrown}");
            StringAssert.Contains(invalidOperation.Message, CorsConfigKeys.AdminAllowedOrigins);
        }
        finally
        {
            Environment.SetEnvironmentVariable(adminOriginsKey, previous);
        }
    }

    /// <summary>Host-startup failures surface wrapped (TargetInvocationException / AggregateException,
    /// depending on how the test host's entry-point invocation faults) — walk InnerException to the
    /// innermost InvalidOperationException instead of asserting on the outer wrapper's type/message.</summary>
    private static InvalidOperationException? InnermostInvalidOperationException(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException invalidOperation)
            {
                return invalidOperation;
            }
        }
        return null;
    }
}
