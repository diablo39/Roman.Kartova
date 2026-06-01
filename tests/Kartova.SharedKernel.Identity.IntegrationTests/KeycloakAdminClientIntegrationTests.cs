using Kartova.SharedKernel.Identity;
using Kartova.Testing.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kartova.SharedKernel.Identity.IntegrationTests;

/// <summary>
/// Slice 9 / Phase H1 batch 5 — direct integration tests for
/// <see cref="KeycloakAdminClient"/> against a real Keycloak Testcontainer.
/// <para>
/// Phase A (slice-9) added the production client; the Organization integration
/// tests in H1 batches 1 and 4 exercise it indirectly through invitation and
/// member-management handlers. This suite locks the HTTP wire contract
/// (request shape, response parsing, error mapping) independently of any
/// consumer so a regression in the client surfaces here first — see slice-9
/// spec §11.3, "Phase A carry-forward".
/// </para>
/// <para>
/// The container is shared across the class via
/// <c>[ClassInitialize]</c>/<c>[ClassCleanup]</c> — KC boot is ~10-20s, per-test
/// execution is ~1-3s once warm. Every test uses a fresh email
/// (<c>$"int-test-{Guid.NewGuid():N}@kartova.local"</c>) and cleans up the
/// created user in a <c>finally</c> block so orphans don't leak between tests.
/// </para>
/// </summary>
[TestClass]
public sealed class KeycloakAdminClientIntegrationTests
{
    private static KeycloakContainerFixture? _kc;
    private static ServiceProvider? _services;

    [ClassInitialize]
    public static async Task InitAsync(TestContext _)
    {
        _kc = new KeycloakContainerFixture();
        await _kc.InitializeAsync();

        // Wire DI exactly the way production composes the client — going through
        // AddKeycloakAdminClient rather than constructing KeycloakAdminClient
        // directly also locks the registration contract (options binding,
        // HttpClient factory, TokenClient wiring) under integration coverage.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KartovaIdentity:Keycloak:BaseUrl"] = _kc.KeycloakBaseUrl,
                ["KartovaIdentity:Keycloak:Realm"] = RealmSeedConstants.RealmName,
                ["KartovaIdentity:Keycloak:AdminClientId"] = RealmSeedConstants.AdminClientId,
                ["KartovaIdentity:Keycloak:AdminClientSecret"] = RealmSeedConstants.AdminClientSecret,
                ["KartovaIdentity:Keycloak:FrontendBaseUrl"] = "http://localhost:5173",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        // KeycloakAdminOptionsEnvValidator (added in slice-9 H2) takes IHostEnvironment via DI.
        // This suite builds its ServiceCollection by hand rather than going through
        // WebApplicationFactory<Program>, so the host-environment registration that production
        // gets for free has to be supplied explicitly. Development matches the FrontendBaseUrl
        // = http://localhost:5173 we configure above (the validator allow-lists localhost in
        // Development and Testing envs only — see KeycloakAdminOptionsEnvValidator).
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment(Environments.Development));
        services.AddKeycloakAdminClient(config);
        _services = services.BuildServiceProvider();
    }

    [ClassCleanup]
    public static async Task DisposeAsync()
    {
        _services?.Dispose();
        if (_kc is not null) await _kc.DisposeAsync();
    }

    private static IKeycloakAdminClient GetClient() =>
        _services!.GetRequiredService<IKeycloakAdminClient>();

    private static string FreshEmail(string scenario) =>
        $"int-test-{scenario}-{Guid.NewGuid():N}@kartova.local";

    private static CreateKeycloakUserRequest NewUserRequest(string email, string? tenantId = null) =>
        new(
            Email: email,
            FirstName: "Integration",
            LastName: "Test",
            TenantId: tenantId ?? Guid.NewGuid().ToString(),
            RequiredActions: new[] { "UPDATE_PASSWORD" });

    private static async Task TryDeleteAsync(Guid? userId)
    {
        if (userId is null) return;
        try
        {
            await GetClient().DeleteUserAsync(userId.Value, CancellationToken.None);
        }
        catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound)
        {
            // Idempotent — test may have already deleted the user.
        }
    }

    [TestMethod]
    public async Task CreateUser_returns_id_and_GetUser_returns_the_created_user()
    {
        // NOTE on tenant-id attribute: KeycloakAdminClient writes the tenantId attribute
        // on create ("tenantId" -> [request.TenantId]) and KC persists it, but KC 26's
        // GET /users/{id} omits the custom "attributes" bag by default unless
        // ?userProfileMetadata=true is passed or the attribute is declared on the
        // user-profile config. The KeycloakUser.TenantId field was therefore dropped
        // (H1 carry-forward) — consumers read the canonical tenant id off their own DB
        // row, never off the KC representation. This test now only round-trips the
        // fields KC reliably echoes back: id, email, enabled, emailVerified.
        var client = GetClient();
        var email = FreshEmail("roundtrip");
        Guid? createdId = null;
        try
        {
            createdId = await client.CreateUserAsync(
                NewUserRequest(email, tenantId: Guid.NewGuid().ToString()),
                CancellationToken.None);
            Assert.AreNotEqual(Guid.Empty, createdId.Value);

            var fetched = await client.GetUserAsync(createdId.Value, CancellationToken.None);

            Assert.IsNotNull(fetched);
            Assert.AreEqual(createdId.Value, fetched!.Id);
            Assert.AreEqual(email, fetched.Email);
            Assert.IsTrue(fetched.Enabled);
            Assert.IsFalse(fetched.EmailVerified);
        }
        finally
        {
            await TryDeleteAsync(createdId);
        }
    }

    [TestMethod]
    public async Task CreateUser_with_duplicate_email_throws_EmailAlreadyExists()
    {
        var client = GetClient();
        var email = FreshEmail("duplicate");
        Guid? createdId = null;
        try
        {
            createdId = await client.CreateUserAsync(NewUserRequest(email), CancellationToken.None);

            var ex = await Assert.ThrowsExactlyAsync<KeycloakAdminException>(() =>
                client.CreateUserAsync(NewUserRequest(email), CancellationToken.None));
            Assert.AreEqual(KeycloakAdminError.EmailAlreadyExists, ex.Error);
        }
        finally
        {
            await TryDeleteAsync(createdId);
        }
    }

    [TestMethod]
    public async Task DeleteUser_then_GetUser_returns_null()
    {
        var client = GetClient();
        var email = FreshEmail("delete");
        var createdId = await client.CreateUserAsync(NewUserRequest(email), CancellationToken.None);

        await client.DeleteUserAsync(createdId, CancellationToken.None);

        var fetched = await client.GetUserAsync(createdId, CancellationToken.None);
        Assert.IsNull(fetched);
    }

    [TestMethod]
    public async Task AssignRealmRole_to_user_succeeds_when_role_exists()
    {
        // "Member" is one of the realm roles seeded in deploy/keycloak/kartova-realm.json
        // (alongside OrgAdmin, TeamAdmin, Viewer, platform-admin). RealmSeedConstants does
        // not hold role names — they're a per-test concern — but the realm seed must continue
        // to define this role; if it disappears, this test fails with KeycloakAdminError.NotFound.
        var client = GetClient();
        var email = FreshEmail("assignrole");
        Guid? createdId = null;
        try
        {
            createdId = await client.CreateUserAsync(NewUserRequest(email), CancellationToken.None);

            // No-throw assertion is sufficient: GetUserAsync does not echo role mappings
            // (KC's GET /users/{id} returns the user representation without realm-role
            // expansion unless ?briefRepresentation=false&userProfileMetadata=true and a
            // role-mappings sub-resource fetch). The wire-contract test is "no exception".
            await client.AssignRealmRoleAsync(createdId.Value, "Member", CancellationToken.None);
        }
        finally
        {
            await TryDeleteAsync(createdId);
        }
    }

    [TestMethod]
    public async Task SearchUsers_returns_user_matching_email_or_username()
    {
        var client = GetClient();
        var unique = Guid.NewGuid().ToString("N");
        var email = $"searchable-{unique}@kartova.local";
        Guid? createdId = null;
        try
        {
            createdId = await client.CreateUserAsync(NewUserRequest(email), CancellationToken.None);

            var hits = await client.SearchUsersAsync($"searchable-{unique}", 10, CancellationToken.None);

            Assert.IsNotNull(hits);
            Assert.IsTrue(hits.Count >= 1, $"Expected at least one match for the unique fragment, got {hits.Count}.");
            Assert.IsTrue(
                hits.Any(u => u.Id == createdId && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)),
                "Expected the created user (by id + email) to appear in the search results.");
        }
        finally
        {
            await TryDeleteAsync(createdId);
        }
    }

    [TestMethod]
    public async Task SetPasswordAsync_then_UpdateUserAsync_finalizes_invited_user()
    {
        var client = GetClient();
        Guid? createdId = null;
        try
        {
            createdId = await client.CreateUserAsync(new CreateKeycloakUserRequest(
                FreshEmail("setpw"),
                null, null,
                Guid.NewGuid().ToString(),
                new[] { KeycloakAdminRequiredActions.UpdatePassword }), CancellationToken.None);

            await client.SetPasswordAsync(createdId.Value, "Sup3rSecretPassw0rd!", temporary: false, CancellationToken.None);
            await client.UpdateUserAsync(createdId.Value, new UpdateKeycloakUserRequest(
                FirstName: "Jane Doe",
                LastName: null,
                EmailVerified: true,
                RequiredActions: Array.Empty<string>()), CancellationToken.None);

            var user = await client.GetUserAsync(createdId.Value, CancellationToken.None);
            Assert.IsNotNull(user);
            Assert.IsTrue(user!.EmailVerified);
            Assert.AreEqual("Jane Doe", user.FirstName);
        }
        finally
        {
            await TryDeleteAsync(createdId);
        }
    }

    [TestMethod]
    public async Task SetPasswordAsync_throws_NotFound_for_unknown_user()
    {
        var client = GetClient();
        var ex = await Assert.ThrowsExactlyAsync<KeycloakAdminException>(() =>
            client.SetPasswordAsync(Guid.NewGuid(), "whatever12345", false, CancellationToken.None));
        Assert.AreEqual(KeycloakAdminError.NotFound, ex.Error);
    }

    /// <summary>
    /// Regression guard for FIX A: verifies that <see cref="KeycloakAdminClient.CreateUserAsync"/>
    /// writes the user attribute as <c>tenant_id</c> (snake_case), not <c>tenantId</c> (camelCase),
    /// so the token mapper (<c>user.attribute: tenant_id</c>) can read it and emit the claim.
    /// <para>
    /// The test reads the attribute back via the raw KC admin REST API
    /// (<c>GET /admin/realms/{realm}/users/{id}?userProfileMetadata=true</c>) as a
    /// <c>JsonDocument</c> because <see cref="KeycloakUser"/> deliberately omits the
    /// attributes bag (consumers read tenant id from their own DB row, not from KC).
    /// Fix B (unmanagedAttributePolicy = ENABLED in the realm seed) is a pre-condition:
    /// without it, KC silently drops the unmanaged attribute on the profile save and
    /// this assertion would fail with "tenant_id array is null or empty".
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task CreateUser_persists_tenant_id_attribute_with_snake_case_key()
    {
        var client = GetClient();
        var email = FreshEmail("tenantattr");
        var expectedTenantId = Guid.NewGuid().ToString();
        Guid? createdId = null;
        try
        {
            createdId = await client.CreateUserAsync(
                NewUserRequest(email, tenantId: expectedTenantId),
                CancellationToken.None);
            Assert.AreNotEqual(Guid.Empty, createdId.Value);

            // Fetch the raw KC user representation via the admin REST API — we need the
            // attributes bag, which KeycloakUser omits by design.  An admin token is
            // obtained the same way GetTokenAsync does it inside the production client
            // (client-credentials against the kartova-admin service account).
            // We reach straight to the Keycloak HTTP base URL, which the fixture exposes
            // as _kc.KeycloakBaseUrl, and use HttpClient directly to keep this test
            // self-contained without coupling to non-public internals of KeycloakAdminClient.
            using var httpClient = new System.Net.Http.HttpClient { BaseAddress = new Uri(_kc!.KeycloakBaseUrl) };

            // Obtain an admin token via client-credentials (same client used by the production client).
            using var tokenReq = new System.Net.Http.FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type",    "client_credentials"),
                new System.Collections.Generic.KeyValuePair<string, string>("client_id",     RealmSeedConstants.AdminClientId),
                new System.Collections.Generic.KeyValuePair<string, string>("client_secret", RealmSeedConstants.AdminClientSecret),
            });
            using var tokenResp = await httpClient.PostAsync(
                $"/realms/{RealmSeedConstants.RealmName}/protocol/openid-connect/token", tokenReq);
            tokenResp.EnsureSuccessStatusCode();

            using var tokenDoc = await System.Text.Json.JsonDocument.ParseAsync(
                await tokenResp.Content.ReadAsStreamAsync());
            var adminToken = tokenDoc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Token response missing access_token.");

            // GET the raw user representation including attributes.
            // userProfileMetadata=true ensures KC returns the full attribute bag even for
            // unmanaged attributes when unmanagedAttributePolicy = ENABLED.
            using var userReq = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get,
                $"/admin/realms/{RealmSeedConstants.RealmName}/users/{createdId.Value}?userProfileMetadata=true");
            userReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

            using var userResp = await httpClient.SendAsync(userReq);
            userResp.EnsureSuccessStatusCode();

            using var userDoc = await System.Text.Json.JsonDocument.ParseAsync(
                await userResp.Content.ReadAsStreamAsync());

            // Assert that attributes.tenant_id[0] equals what we passed in — not
            // "tenantId" (the pre-fix camelCase key that the mapper couldn't read).
            Assert.IsTrue(
                userDoc.RootElement.TryGetProperty("attributes", out var attrsEl),
                "KC user representation missing 'attributes' — unmanagedAttributePolicy may not be ENABLED in the test realm.");
            Assert.IsTrue(
                attrsEl.TryGetProperty("tenant_id", out var tenantIdEl),
                "Attribute 'tenant_id' not found. CreateUserAsync may still be writing 'tenantId' (camelCase) — check the fix.");
            var values = tenantIdEl.EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.IsTrue(values.Length >= 1, "tenant_id attribute array is empty.");
            Assert.AreEqual(expectedTenantId, values[0],
                $"tenant_id attribute value mismatch: expected '{expectedTenantId}', got '{values[0]}'.");
        }
        finally
        {
            await TryDeleteAsync(createdId);
        }
    }

    // Mirrors the StubHostEnvironment in KeycloakAdminOptionsValidationTests (unit-test sibling).
    // Kept inline rather than promoted to a shared helper because the unit tests still need
    // their own copy (they construct it with varying EnvironmentName values per test case),
    // and a two-line stub doesn't justify a new shared project.
    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Kartova.SharedKernel.Identity.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
