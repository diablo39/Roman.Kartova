using Kartova.SharedKernel.Identity;
using Kartova.Testing.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
}
