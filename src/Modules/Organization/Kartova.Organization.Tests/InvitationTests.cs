using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

[TestClass]
public sealed class InvitationTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid InvitedBy = Guid.NewGuid();
    private static readonly Guid KcUser = Guid.NewGuid();

    [TestMethod]
    public void Create_sets_pending_status_and_7day_expiry()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var inv = Invitation.Create("Alice@Example.com", KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH");
        Assert.AreEqual(InvitationStatus.Pending, inv.Status);
        Assert.AreEqual("alice@example.com", inv.Email);
        Assert.AreEqual("Member", inv.Role);
        Assert.AreEqual(InvitedBy, inv.InvitedByUserId);
        Assert.AreEqual(KcUser, inv.KeycloakUserId);
        Assert.AreEqual(DateTimeOffset.Parse("2026-05-27T10:00:00Z"), inv.InvitedAt);
        Assert.AreEqual(DateTimeOffset.Parse("2026-06-03T10:00:00Z"), inv.ExpiresAt);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-an-email")]
    public void Create_rejects_invalid_email(string email)
    {
        var clock = new FakeTimeProvider();
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            Invitation.Create(email, KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH"));
        Assert.AreEqual("email", ex.ParamName);
    }

    [TestMethod]
    public void Create_rejects_unknown_role()
    {
        var clock = new FakeTimeProvider();
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            Invitation.Create("a@b.c", "BogusRole", InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH"));
        Assert.AreEqual("role", ex.ParamName);
    }

    [TestMethod]
    public void Create_rejects_email_longer_than_320_chars()
    {
        var clock = new FakeTimeProvider();
        var tooLong = new string('a', 315) + "@b.com"; // 315 + 6 = 321 chars (>320)
        Assert.AreEqual(321, tooLong.Length);
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            Invitation.Create(tooLong, KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH"));
        Assert.AreEqual("email", ex.ParamName);
    }

    [TestMethod]
    public void Create_accepts_email_at_320_chars()
    {
        var clock = new FakeTimeProvider();
        var maxEmail = new string('a', 314) + "@b.com"; // 314 + 6 = 320 chars
        Assert.AreEqual(320, maxEmail.Length);
        var inv = Invitation.Create(maxEmail, KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH");
        Assert.AreEqual(320, inv.Email.Length);
    }

    [TestMethod]
    public void MarkAccepted_flips_status_and_sets_AcceptedAt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var inv = Invitation.Create("a@b.c", KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH");
        clock.Advance(TimeSpan.FromMinutes(5));
        inv.MarkAccepted(clock);
        Assert.AreEqual(InvitationStatus.Accepted, inv.Status);
        Assert.AreEqual(DateTimeOffset.Parse("2026-05-27T10:05:00Z"), inv.AcceptedAt);
    }

    [TestMethod]
    public void MarkAccepted_throws_when_already_accepted()
    {
        var clock = new FakeTimeProvider();
        var inv = Invitation.Create("a@b.c", KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH");
        inv.MarkAccepted(clock);
        Assert.ThrowsExactly<InvalidOperationException>(() => inv.MarkAccepted(clock));
    }

    [TestMethod]
    public void Revoke_flips_status_and_sets_RevokedAt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var inv = Invitation.Create("a@b.c", KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH");
        clock.Advance(TimeSpan.FromHours(2));
        inv.Revoke(clock);
        Assert.AreEqual(InvitationStatus.Revoked, inv.Status);
        Assert.AreEqual(DateTimeOffset.Parse("2026-05-27T12:00:00Z"), inv.RevokedAt);
    }

    [TestMethod]
    public void Revoke_throws_when_already_terminal()
    {
        var clock = new FakeTimeProvider();
        var inv = Invitation.Create("a@b.c", KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH");
        inv.MarkAccepted(clock);
        Assert.ThrowsExactly<InvalidOperationException>(() => inv.Revoke(clock));
    }

    [TestMethod]
    public void MarkExpired_flips_status()
    {
        var clock = new FakeTimeProvider();
        var inv = Invitation.Create("a@b.c", KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH");
        inv.MarkExpired(clock);
        Assert.AreEqual(InvitationStatus.Expired, inv.Status);
    }

    [TestMethod]
    public void MarkExpired_throws_when_not_pending()
    {
        var clock = new FakeTimeProvider();
        var inv = Invitation.Create("a@b.c", KartovaRoles.Member, InvitedBy, KcUser, Tenant, clock, tokenHash: "HASH");
        inv.Revoke(clock);
        Assert.ThrowsExactly<InvalidOperationException>(() => inv.MarkExpired(clock));
    }

    [TestMethod]
    public void Create_stores_token_hash_and_leaves_credential_unset()
    {
        var clock = new FakeTimeProvider();
        var inv = Invitation.Create("a@b.com", KartovaRoles.Member, Guid.NewGuid(),
            Guid.NewGuid(), new TenantId(Guid.NewGuid()), clock, tokenHash: "HASH");
        Assert.AreEqual("HASH", inv.TokenHash);
        Assert.IsNull(inv.CredentialSetAt);
    }

    [TestMethod]
    public void MarkCredentialSet_burns_token_and_stamps_time()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var inv = Invitation.Create("a@b.com", KartovaRoles.Member, Guid.NewGuid(),
            Guid.NewGuid(), new TenantId(Guid.NewGuid()), clock, tokenHash: "HASH");
        clock.Advance(TimeSpan.FromMinutes(10));
        inv.MarkCredentialSet(clock);
        Assert.IsNull(inv.TokenHash);
        Assert.IsNotNull(inv.CredentialSetAt);
        Assert.AreEqual(InvitationStatus.Pending, inv.Status);
    }

    [TestMethod]
    public void MarkCredentialSet_throws_when_not_pending()
    {
        // Arrange: move invitation to a non-Pending state.
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var inv = Invitation.Create("a@b.com", KartovaRoles.Member, Guid.NewGuid(),
            Guid.NewGuid(), new TenantId(Guid.NewGuid()), clock, tokenHash: "HASH");
        inv.MarkAccepted(clock);

        // Act + Assert: guard must fire.
        Assert.ThrowsExactly<InvalidOperationException>(() => inv.MarkCredentialSet(clock));
    }

    [TestMethod]
    public void MarkCredentialSet_throws_on_second_call_even_when_still_pending()
    {
        // Arrange: Pending invitation with first call succeeding.
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var inv = Invitation.Create("a@b.com", KartovaRoles.Member, Guid.NewGuid(),
            Guid.NewGuid(), new TenantId(Guid.NewGuid()), clock, tokenHash: "HASH");

        // First call must succeed.
        inv.MarkCredentialSet(clock);
        Assert.IsNotNull(inv.CredentialSetAt);

        // Second call must throw — credential already set.
        Assert.ThrowsExactly<InvalidOperationException>(() => inv.MarkCredentialSet(clock));
    }
}
