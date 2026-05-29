using System.Net;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for the slice-9 session-bootstrap endpoint
/// <c>POST /api/v1/auth/session</c> (spec §11.3 scenarios #7 + #8) — verifies the
/// post-auth hook's invitation-acceptance side effect end-to-end against the real
/// Postgres + WebApplicationFactory pipeline. The handler itself is unit-tested
/// at <see cref="Infrastructure.Tests.SessionStartHandlerTests"/>; these tests
/// cover the request-pipeline integration that the unit tests can't reach:
/// claim → <c>OrganizationPostAuthSyncHook</c> → DB flip → handler response.
///
/// Key technical gotcha: the post-auth hook short-circuits when the JWT lacks an
/// <c>email</c> claim (<see cref="OrganizationPostAuthSyncHook"/> at
/// PostAuthHook.cs:43-49). H1 batch 4 extended <see cref="TestJwtSigner"/> +
/// <see cref="KartovaApiFixtureBase.CreateAuthenticatedClientAsync"/> with an
/// optional <c>emailClaim</c> parameter so these tests can mint the right shape.
/// Tests not exercising the hook continue to pass without an email claim — the
/// default is opt-out.
/// </summary>
[TestClass]
public sealed class SessionBootstrapTests : OrganizationIntegrationTestBase
{
    /// <summary>
    /// Common scenario seed for the two session-bootstrap tests: a tenant + org row,
    /// an inviter user row, and a Pending invitation whose
    /// <c>KeycloakUserId</c> equals the <c>sub</c> claim that the invitee's JWT
    /// will carry. Returned tuple lets each test mint the invitee client and clean
    /// up afterwards (each cleanup hook is exception-safe).
    /// </summary>
    private static async Task<SessionBootstrapSeed> SeedAsync(string scenarioSlug)
    {
        var (adminEmail, tenantId) = await NewTenantAsync(scenarioSlug);
        var domain = adminEmail.Split('@')[1];
        var inviterUserId = await Fx.SeedUserInOrganizationAsync(
            tenantId, "Inviter Display", $"inviter@{domain}");

        var inviteeEmail = $"invitee@{domain}";
        var inviteeKcId = Guid.NewGuid();
        // Backdate invitedAt so AcceptedAt > InvitedAt is observable when the
        // hook fires in the request. ExpiresAt defaults to invited_at + 7d (well
        // in the future) so the hook's expiry guard at PostAuthHook.cs:57
        // (ExpiresAt > now) lets the acceptance branch run.
        var invitedAt = TimeProvider.System.GetUtcNow().AddMinutes(-10);
        var invitationId = await Fx.SeedInvitationAsync(
            tenantId: tenantId,
            email: inviteeEmail,
            role: KartovaRoles.Member,
            invitedByUserId: inviterUserId,
            keycloakUserId: inviteeKcId,
            invitedAt: invitedAt);

        return new SessionBootstrapSeed(
            ScenarioSlug: scenarioSlug,
            TenantId: tenantId,
            OrgDisplayName: $"Org-{scenarioSlug}",
            InviterUserId: inviterUserId,
            InviteeEmail: inviteeEmail,
            InviteeKcUserId: inviteeKcId,
            InvitationId: invitationId,
            InvitedAt: invitedAt);
    }

    private sealed record SessionBootstrapSeed(
        string ScenarioSlug,
        Guid TenantId,
        string OrgDisplayName,
        Guid InviterUserId,
        string InviteeEmail,
        Guid InviteeKcUserId,
        Guid InvitationId,
        DateTimeOffset InvitedAt);

    /// <summary>
    /// Tears down everything the seed planted plus the row the post-auth hook
    /// upserts for the invitee. Cleanup order: invitations first (mirrors
    /// <see cref="KartovaApiFixture.DeleteInvitationsForTenantAsync"/>'s internal
    /// contract), users second (no FK from users to invitations or vice versa,
    /// so either order works), organizations last (parent row). Each step in
    /// its own try/catch so a leak on one row doesn't strand the next.
    /// </summary>
    private static async Task CleanupAsync(SessionBootstrapSeed seed)
    {
#pragma warning disable CA1031 // best-effort test teardown — log and continue
        try { await Fx.DeleteInvitationsForTenantAsync(seed.TenantId); }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] invitations delete failed for tenant {seed.TenantId}: {ex.Message}");
        }
        try
        {
            await using var db = new OrganizationDbContext(BypassOptions());
            // Wipe both inviter + invitee rows (the latter is hook-upserted, so the
            // test never sees the userId directly — clearing by tenant is robust).
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM users WHERE tenant_id = {0}", seed.TenantId);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] users delete failed for tenant {seed.TenantId}: {ex.Message}");
        }
        try { await Fx.DeleteOrganizationsForTenantAsync(seed.TenantId); }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] organizations delete failed for tenant {seed.TenantId}: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    // ---------- Scenario #7 (spec §11.3): first session-start flips invitation -----

    [TestMethod]
    public async Task Session_start_after_invitation_login_marks_accepted()
    {
        var seed = await SeedAsync("session-bootstrap-7");
        try
        {
            // Impersonate the invitee — JWT sub = the KC user id stored on the
            // invitation, email = the invitation email. Role = Member (the role
            // the invitation carried). The TestJwtSigner emits the email claim
            // because emailClaim is non-null; without it the post-auth hook
            // short-circuits and the test would fail.
            var inviteeClient = await Fx.CreateAuthenticatedClientAsync(
                seed.InviteeEmail,
                roles: new[] { KartovaRoles.Member },
                subjectOverride: seed.InviteeKcUserId,
                emailClaim: seed.InviteeEmail);

            var resp = await inviteeClient.PostAsync("/api/v1/auth/session", content: null);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<SessionStartResponse>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);

            // Me: hook upserted the row, OrganizationUserDirectory returned it.
            Assert.AreEqual(seed.InviteeKcUserId, body!.Me.Id);
            Assert.AreEqual(seed.InviteeEmail, body.Me.Email);

            // Role + permissions: the Member set must be present (not the empty
            // PlatformAdmin fallback, not the Viewer fallback). Member includes
            // TeamRead — assert that specifically to kill mutants that swap the
            // role lookup for the Viewer set (also has TeamRead) by also asserting
            // a Member-specific permission (CatalogApplicationsRegister) that
            // Viewer does NOT carry.
            Assert.AreEqual(KartovaRoles.Member, body.Role);
            CollectionAssert.Contains(body.Permissions.ToArray(), KartovaPermissions.TeamRead);
            CollectionAssert.Contains(
                body.Permissions.ToArray(), KartovaPermissions.CatalogApplicationsRegister);

            // Org profile: came from the NewTenantAsync seed.
            Assert.AreEqual(seed.OrgDisplayName, body.Organization.DisplayName);

            // AcceptedInvitation: present + matches the seeded invitation.
            Assert.IsNotNull(body.AcceptedInvitation);
            Assert.AreEqual(seed.OrgDisplayName, body.AcceptedInvitation!.OrgDisplayName);
            Assert.AreEqual(seed.InviterUserId, body.AcceptedInvitation.InvitedBy.Id);
            Assert.AreEqual("Inviter Display", body.AcceptedInvitation.InvitedBy.DisplayName);

            // InvitedAt: should match the seeded value within DB rounding (Postgres
            // stores DateTimeOffset as microseconds). One-second tolerance is more
            // than enough.
            var invitedDelta = (body.AcceptedInvitation.InvitedAt - seed.InvitedAt).Duration();
            Assert.IsTrue(
                invitedDelta < TimeSpan.FromSeconds(1),
                $"InvitedAt should match seed within 1s; actual delta = {invitedDelta}.");

            // AcceptedAt: set by MarkAccepted in this request, must be after
            // InvitedAt and within ~10s of "now" (allow container clock drift).
            Assert.IsTrue(
                body.AcceptedInvitation.AcceptedAt > body.AcceptedInvitation.InvitedAt,
                $"AcceptedAt ({body.AcceptedInvitation.AcceptedAt:o}) must be after " +
                $"InvitedAt ({body.AcceptedInvitation.InvitedAt:o}).");
            var acceptedAge = TimeProvider.System.GetUtcNow() - body.AcceptedInvitation.AcceptedAt;
            Assert.IsTrue(
                acceptedAge < TimeSpan.FromSeconds(10) && acceptedAge >= TimeSpan.Zero,
                $"AcceptedAt must be recent; observed age = {acceptedAge}.");

            // DB: status flipped + AcceptedAt populated.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                var inv = await db.Invitations.SingleAsync(
                    i => EF.Property<Guid>(i, "_id") == seed.InvitationId);
                Assert.AreEqual(InvitationStatus.Accepted, inv.Status);
                Assert.IsNotNull(inv.AcceptedAt);

                // Users projection: hook upserted the invitee row keyed by
                // KeycloakUserId.
                var userRow = await db.Users.SingleAsync(u => u.Id == seed.InviteeKcUserId);
                Assert.AreEqual(seed.InviteeEmail, userRow.Email);
                Assert.AreEqual(new TenantId(seed.TenantId), userRow.TenantId);
            }
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    // ---------- Scenario #8 (spec §11.3): subsequent call has no welcome ----------

    [TestMethod]
    public async Task Session_start_subsequent_call_returns_no_accepted_invitation()
    {
        var seed = await SeedAsync("session-bootstrap-8");
        try
        {
            var inviteeClient = await Fx.CreateAuthenticatedClientAsync(
                seed.InviteeEmail,
                roles: new[] { KartovaRoles.Member },
                subjectOverride: seed.InviteeKcUserId,
                emailClaim: seed.InviteeEmail);

            // First call: flips Pending → Accepted via the post-auth hook. We
            // don't assert the welcome payload here — that's #7's job. We just
            // need the state transition to happen so the second call observes
            // the Accepted row.
            var first = await inviteeClient.PostAsync("/api/v1/auth/session", content: null);
            Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

            // Second call: same client, same JWT. The post-auth hook now finds no
            // Pending invitation (the row is Accepted from the first call), so it
            // does NOT call SetJustAcceptedInvitation, so ICurrentUser
            // .JustAcceptedInvitationId stays null, so the handler emits a null
            // AcceptedInvitation block.
            var second = await inviteeClient.PostAsync("/api/v1/auth/session", content: null);
            Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

            var body = await second.Content.ReadFromJsonAsync<SessionStartResponse>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);

            // Core payload still valid — only the welcome block differs from #7.
            Assert.AreEqual(seed.InviteeKcUserId, body!.Me.Id);
            Assert.AreEqual(KartovaRoles.Member, body.Role);
            Assert.IsTrue(
                body.Permissions.Count > 0,
                "Member role must yield a non-empty permission set even on subsequent session-starts.");
            CollectionAssert.Contains(body.Permissions.ToArray(), KartovaPermissions.TeamRead);

            Assert.IsNull(
                body.AcceptedInvitation,
                "AcceptedInvitation must be null on the second session-start — the welcome banner " +
                "is a one-shot signal driven by the in-request invitation flip, not the persisted status.");

            // DB: status is STILL Accepted from the first call (no re-flip — the
            // hook only acts on Pending invitations; the second-call's hook found
            // no Pending row and short-circuited).
            await using var db = new OrganizationDbContext(BypassOptions());
            var inv = await db.Invitations.SingleAsync(
                i => EF.Property<Guid>(i, "_id") == seed.InvitationId);
            Assert.AreEqual(InvitationStatus.Accepted, inv.Status);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }
}
