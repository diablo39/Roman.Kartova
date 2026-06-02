using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for the anonymous invitation-accept endpoints (spec §6.9 / §11.3):
/// <list type="bullet">
/// <item><c>GET /api/v1/invitations/accept?token=&lt;t&gt;</c> — returns context DTO.</item>
/// <item><c>POST /api/v1/invitations/accept</c> — sets KC password, burns token, leaves Pending.</item>
/// </list>
/// Both endpoints are anonymous (no JWT, no tenant scope) and live in
/// Infrastructure.Admin behind the BYPASSRLS pool (cross-tenant token lookup).
/// The fixture's Keycloak container is required because the POST handler calls
/// <see cref="IKeycloakAdminClient.SetPasswordAsync"/> and
/// <see cref="IKeycloakAdminClient.UpdateUserAsync"/>.
///
/// Seeding strategy: invitations are created via the real
/// <c>POST /api/v1/organizations/invitations</c> endpoint (OrgAdmin JWT) so the
/// token plaintext flows through the same issuance path as production. The
/// plaintext is extracted from the returned <c>InviteUrl</c>; only the hash is
/// ever stored in the DB.
/// </summary>
[TestClass]
public sealed class InvitationAcceptTests : OrganizationIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates an invitation via the real HTTP endpoint and returns the plaintext token.
    /// Callers must pass an OrgAdmin-authenticated client. The returned token is
    /// extracted from the <c>inviteUrl</c> query string so it exercises the same
    /// issuance path as production.
    /// </summary>
    private static async Task<(Guid InvitationId, string Token, Guid KcUserId)> CreateInvitationAsync(
        HttpClient adminClient,
        string inviteeEmail,
        string role = KartovaRoles.Member)
    {
        var resp = await adminClient.PostAsJsonAsync(
            "/api/v1/organizations/invitations",
            new CreateInvitationRequest(inviteeEmail, role));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode,
            $"Expected 201 from CreateInvitation for {inviteeEmail}; got {(int)resp.StatusCode}.");

        var body = await resp.Content.ReadFromJsonAsync<CreateInvitationResponse>(
            KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(body, "CreateInvitation response body must not be null.");

        // InviteUrl format: "{frontendBase}/accept-invitation?token={plaintext}"
        // We extract the token= query param.
        var uri = new Uri(body!.InviteUrl);
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var token = queryParams["token"];
        Assert.IsFalse(string.IsNullOrWhiteSpace(token),
            $"InviteUrl must contain a token= param. Actual URL: {body.InviteUrl}");

        // Read the KC user id from the DB (same pattern as InvitationTests).
        await using var db = new OrganizationDbContext(BypassOptions());
        var kcUserId = await db.Invitations
            .Where(i => EF.Property<Guid>(i, "_id") == body.Invitation.Id)
            .Select(i => i.KeycloakUserId)
            .SingleAsync();
        Assert.IsNotNull(kcUserId, "Freshly-created invitation must have a KC user id.");

        return (body.Invitation.Id, token!, kcUserId!.Value);
    }

    /// <summary>
    /// Best-effort cleanup: users + invitations rows and the KC user.
    /// Mirrors the cleanup pattern in <see cref="InvitationTests"/>.
    /// </summary>
    private static async Task CleanupTenantAsync(Guid tenantId, Guid? kcUserId = null)
    {
#pragma warning disable CA1031 // best-effort test teardown — log and continue
        try
        {
            await using var db = new OrganizationDbContext(BypassOptions());
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM users WHERE tenant_id = {0}", tenantId);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] users delete failed for tenant {tenantId}: {ex.Message}");
        }

        try { await Fx.DeleteInvitationsForTenantAsync(tenantId); }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] invitations delete failed for tenant {tenantId}: {ex.Message}");
        }

        if (kcUserId is not null)
        {
            using var kcScope = Fx.Services.CreateScope();
            var kc = kcScope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();
            try { await kc.DeleteUserAsync(kcUserId.Value, CancellationToken.None); }
            catch { }
        }
#pragma warning restore CA1031
    }

    // -------------------------------------------------------------------------
    // Test #1 — GET /accept?token=<valid> → 200, body shape correct
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Get_context_with_valid_token_returns_200_and_seeded_org_email_and_role()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("accept-get-200");
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@{adminEmail.Split('@')[1]}";

        Guid? kcUserId = null;
        try
        {
            var adminClient = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });
            var (_, token, kc) = await CreateInvitationAsync(adminClient, inviteeEmail);
            kcUserId = kc;

            var anonClient = Fx.CreateAnonymousClient();
            var resp = await anonClient.GetAsync(
                $"/api/v1/invitations/accept?token={Uri.EscapeDataString(token)}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

            var context = await resp.Content.ReadFromJsonAsync<InvitationAcceptContext>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(context);

            // OrgDisplayName: seeded by NewTenantAsync as "Org-{scenarioSlug}".
            Assert.AreEqual("Org-accept-get-200", context!.OrgDisplayName);
            // Email: handler lowercases; invitation was created from our inviteeEmail.
            Assert.AreEqual(inviteeEmail.ToLowerInvariant(), context.Email);
            // Role: we requested Member.
            Assert.AreEqual(KartovaRoles.Member, context.Role);
            // ExpiresAt must be in the future (defaults to invitedAt + 7d).
            Assert.IsTrue(
                context.ExpiresAt > DateTimeOffset.UtcNow,
                $"ExpiresAt ({context.ExpiresAt:o}) must be in the future.");
        }
        finally
        {
            await CleanupTenantAsync(tenantId, kcUserId);
        }
    }

    // -------------------------------------------------------------------------
    // Test #2 — GET /accept?token=<unknown> → 404
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Get_context_with_unknown_token_returns_404()
    {
        var randomToken = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var anonClient = Fx.CreateAnonymousClient();
        var resp = await anonClient.GetAsync(
            $"/api/v1/invitations/accept?token={Uri.EscapeDataString(randomToken)}");

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Test #3 — GET /accept?token=<revoked-token> → 410
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Get_context_with_revoked_token_returns_410()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("accept-get-410-revoke");
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@{adminEmail.Split('@')[1]}";

        Guid? kcUserId = null;
        try
        {
            var adminClient = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });
            var (invitationId, token, kc) = await CreateInvitationAsync(adminClient, inviteeEmail);
            kcUserId = kc;

            // Revoke via the real revoke endpoint — this also deletes the KC user.
            var revoke = await adminClient.PostAsync(
                $"/api/v1/organizations/invitations/{invitationId}/revoke",
                content: null);
            Assert.AreEqual(HttpStatusCode.NoContent, revoke.StatusCode,
                "Revoke must return 204 so the 410 assertion runs against a genuinely revoked row.");
            // Revoke deleted the KC user — don't try to delete it again in cleanup.
            kcUserId = null;

            var anonClient = Fx.CreateAnonymousClient();
            var resp = await anonClient.GetAsync(
                $"/api/v1/invitations/accept?token={Uri.EscapeDataString(token)}");

            // ResolveAsync returns GoneRevoked when Status == Revoked; the route maps
            // any non-NotFound failed result to 410.
            Assert.AreEqual(HttpStatusCode.Gone, resp.StatusCode);
        }
        finally
        {
            await CleanupTenantAsync(tenantId, kcUserId);
        }
    }

    // -------------------------------------------------------------------------
    // Test #4 — POST /accept {valid token, password, displayName} → 200
    //           DB: token_hash NULL, credential_set_at set, status Pending
    //           KC:  emailVerified=true, firstName="Jane Doe", no UPDATE_PASSWORD
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Post_accept_with_valid_token_returns_200_and_burns_token_and_updates_keycloak()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("accept-post-200");
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@{adminEmail.Split('@')[1]}";

        Guid? kcUserId = null;
        try
        {
            var adminClient = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });
            var (invitationId, token, kc) = await CreateInvitationAsync(adminClient, inviteeEmail);
            kcUserId = kc;

            var anonClient = Fx.CreateAnonymousClient();
            var resp = await anonClient.PostAsJsonAsync(
                "/api/v1/invitations/accept",
                new AcceptInvitationRequest(token, "Sup3rSecretPassw0rd!", "Jane Doe"));

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<AcceptInvitationResponse>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.AreEqual(inviteeEmail.ToLowerInvariant(), body!.Email,
                "Response email must match the invited email (lowercased).");

            // DB assertions — BYPASSRLS so assertions run outside tenant scope.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                var inv = await db.Invitations.SingleAsync(
                    i => EF.Property<Guid>(i, "_id") == invitationId);

                // Token burned (single-use).
                Assert.IsNull(inv.TokenHash,
                    "TokenHash must be NULL after a successful accept (single-use token burned).");
                // CredentialSetAt stamped.
                Assert.IsNotNull(inv.CredentialSetAt,
                    "CredentialSetAt must be populated after a successful accept.");
                // Status stays Pending — flips to Accepted only on first OIDC login (spec §6).
                Assert.AreEqual(InvitationStatus.Pending, inv.Status,
                    "Status must remain Pending after accept — it flips to Accepted on first login.");
            }

            // KC assertions — check via the IKeycloakAdminClient.
            using var kcScope = Fx.Services.CreateScope();
            var kcClient = kcScope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();
            var kcUser = await kcClient.GetUserAsync(kcUserId!.Value, CancellationToken.None);
            Assert.IsNotNull(kcUser, "KC user must still exist after accept.");
            Assert.IsTrue(kcUser!.EmailVerified, "KC user must have emailVerified=true after accept.");
            // Handler passes displayName as FirstName (UpdateKeycloakUserRequest(trimmedName, null, ...)).
            Assert.AreEqual("Jane Doe", kcUser.FirstName,
                "KC user FirstName must be set to the supplied displayName.");

            // requiredActions: UPDATE_PASSWORD must be cleared. The domain KeycloakUser
            // type does not expose requiredActions, so we query KC admin REST directly.
            // The KC container's base URL is accessible via Fx.Keycloak!.KeycloakBaseUrl.
            Assert.IsNotNull(Fx.Keycloak,
                "This test requires UsesKeycloakContainer=true (KC container must be running).");
            var requiredActions = await GetKcRequiredActionsAsync(kcUserId!.Value);
            Assert.AreEqual(0, requiredActions.Count,
                $"KC user must have no required actions after accept. Found: [{string.Join(", ", requiredActions)}].");
        }
        finally
        {
            await CleanupTenantAsync(tenantId, kcUserId);
        }
    }

    // -------------------------------------------------------------------------
    // Test #5 — POST /accept twice with same token → 200 then 404 (burned)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Post_accept_second_call_with_burned_token_returns_404()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("accept-post-reuse");
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@{adminEmail.Split('@')[1]}";

        Guid? kcUserId = null;
        try
        {
            var adminClient = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });
            var (_, token, kc) = await CreateInvitationAsync(adminClient, inviteeEmail);
            kcUserId = kc;

            var anonClient = Fx.CreateAnonymousClient();
            var body = new AcceptInvitationRequest(token, "Sup3rSecretPassw0rd!", "First Attempt");

            // First POST: succeeds, burns the token.
            var first = await anonClient.PostAsJsonAsync("/api/v1/invitations/accept", body);
            Assert.AreEqual(HttpStatusCode.OK, first.StatusCode,
                "First accept must succeed.");

            // Second POST: token_hash is now NULL → ResolveAsync finds no matching
            // row → NotFound (see design note: NOT 410 — the row is simply not found
            // by hash lookup).
            var second = await anonClient.PostAsJsonAsync("/api/v1/invitations/accept", body);
            Assert.AreEqual(HttpStatusCode.NotFound, second.StatusCode,
                "Second accept with the same (burned) token must return 404, not 410.");
        }
        finally
        {
            await CleanupTenantAsync(tenantId, kcUserId);
        }
    }

    // -------------------------------------------------------------------------
    // Test #6 — POST /accept with invalid password or empty displayName → 400
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Post_accept_with_too_short_password_returns_400()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("accept-post-400-pw");
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@{adminEmail.Split('@')[1]}";

        Guid? kcUserId = null;
        try
        {
            var adminClient = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });
            var (_, token, kc) = await CreateInvitationAsync(adminClient, inviteeEmail);
            kcUserId = kc;

            var anonClient = Fx.CreateAnonymousClient();
            // Password "short" is 5 chars, well below MinPasswordLength = 12.
            var resp = await anonClient.PostAsJsonAsync(
                "/api/v1/invitations/accept",
                new AcceptInvitationRequest(token, "short", "Jane Doe"));

            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode,
                "A password shorter than 12 characters must return 400.");
        }
        finally
        {
            // Token is NOT burned (handler returned Validation early, before KC calls).
            await CleanupTenantAsync(tenantId, kcUserId);
        }
    }

    [TestMethod]
    public async Task Post_accept_with_empty_displayName_returns_400()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("accept-post-400-name");
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@{adminEmail.Split('@')[1]}";

        Guid? kcUserId = null;
        try
        {
            var adminClient = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });
            var (_, token, kc) = await CreateInvitationAsync(adminClient, inviteeEmail);
            kcUserId = kc;

            var anonClient = Fx.CreateAnonymousClient();
            // DisplayName is empty — trimmed length = 0, which fails the length guard.
            var resp = await anonClient.PostAsJsonAsync(
                "/api/v1/invitations/accept",
                new AcceptInvitationRequest(token, "Sup3rSecretPassw0rd!", ""));

            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode,
                "An empty display name must return 400.");
        }
        finally
        {
            // Token is NOT burned (handler returned Validation early, before KC calls).
            await CleanupTenantAsync(tenantId, kcUserId);
        }
    }

    // -------------------------------------------------------------------------
    // Test #7 — POST /accept concurrent calls with same token → one 200, one 410
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Post_accept_concurrent_calls_with_same_token_returns_one_200_and_one_410()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("accept-concurrent");
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@{adminEmail.Split('@')[1]}";

        Guid? kcUserId = null;
        try
        {
            var adminClient = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });
            var (_, token, kc) = await CreateInvitationAsync(adminClient, inviteeEmail);
            kcUserId = kc;

            var anonClient1 = Fx.CreateAnonymousClient();
            var anonClient2 = Fx.CreateAnonymousClient();
            var body = new AcceptInvitationRequest(token, "Sup3rSecretPassw0rd!", "Concurrent User");

            // Fire both concurrently — one wins the xmin race, one loses.
            var responses = await Task.WhenAll(
                anonClient1.PostAsJsonAsync("/api/v1/invitations/accept", body),
                anonClient2.PostAsJsonAsync("/api/v1/invitations/accept", body));

            var statuses = new[] { (int)responses[0].StatusCode, (int)responses[1].StatusCode };
            var ok200 = statuses.Count(s => s == 200);
            // With xmin the loser gets 410 (GoneAlreadyUsed). Under rare serialization
            // the loser may see 404 (burned-hash path). Both are valid race outcomes.
            var goneOrNotFound = statuses.Count(s => s is 410 or 404);

            Assert.AreEqual(1, ok200,
                $"Exactly one response must be 200 OK. Got: {statuses[0]}, {statuses[1]}.");
            Assert.AreEqual(1, goneOrNotFound,
                $"Exactly one response must be 410 Gone (or 404 if requests serialized after burn). Got: {statuses[0]}, {statuses[1]}.");
        }
        finally
        {
            await CleanupTenantAsync(tenantId, kcUserId);
        }
    }

    // -------------------------------------------------------------------------
    // Test #8 — GET /accept exceeds rate limit → 429 on 11th request
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Get_accept_exceeds_rate_limit_returns_429()
    {
        // Rate limit: 10 requests/min per remote IP (loopback → same partition for all test requests).
        // Sends 11 sequential GETs with a bogus token; rate limiting applies before the handler.
        // The first 10 return 404 (unknown token); the 11th must return 429.
        var bogusToken = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var anonClient = Fx.CreateAnonymousClient();
        var url = $"/api/v1/invitations/accept?token={Uri.EscapeDataString(bogusToken)}";

        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 11; i++)
        {
            lastResponse = await anonClient.GetAsync(url);
        }

        Assert.AreEqual(System.Net.HttpStatusCode.TooManyRequests, lastResponse!.StatusCode,
            $"The 11th request must be rate-limited (429). Got {(int)lastResponse.StatusCode}.");
    }

    // -------------------------------------------------------------------------
    // Helpers for KC requiredActions (raw admin REST — KeycloakUser omits this field)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fetches the <c>requiredActions</c> list for a KC user by calling the admin REST
    /// API directly. Used by test #4 to verify <c>UPDATE_PASSWORD</c> is cleared after
    /// a successful accept, because <see cref="KeycloakUser"/> (the slim domain
    /// projection) does not expose required-actions.
    /// </summary>
    private static async Task<IReadOnlyList<string>> GetKcRequiredActionsAsync(Guid kcUserId)
    {
        // Obtain a service-account token using client-credentials against the KC container.
        var kcBaseUrl = Fx.Keycloak!.KeycloakBaseUrl;
        var realm = RealmSeedConstants.RealmName;
        var adminClientId = RealmSeedConstants.AdminClientId;
        var adminClientSecret = RealmSeedConstants.AdminClientSecret;

        using var tokenHttp = new HttpClient { BaseAddress = new Uri(kcBaseUrl) };
        var tokenResp = await tokenHttp.PostAsync(
            $"/realms/{realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = adminClientId,
                ["client_secret"] = adminClientSecret,
            }));
        tokenResp.EnsureSuccessStatusCode();
        var tokenDoc = await JsonDocument.ParseAsync(await tokenResp.Content.ReadAsStreamAsync());
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString()!;

        using var adminHttp = new HttpClient { BaseAddress = new Uri(kcBaseUrl) };
        adminHttp.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var userResp = await adminHttp.GetAsync($"/admin/realms/{realm}/users/{kcUserId}");
        userResp.EnsureSuccessStatusCode();

        using var userDoc = await JsonDocument.ParseAsync(await userResp.Content.ReadAsStreamAsync());
        if (!userDoc.RootElement.TryGetProperty("requiredActions", out var actionsEl))
            return Array.Empty<string>();

        var actions = new List<string>();
        foreach (var el in actionsEl.EnumerateArray())
        {
            var s = el.GetString();
            if (s is not null) actions.Add(s);
        }
        return actions;
    }
}
