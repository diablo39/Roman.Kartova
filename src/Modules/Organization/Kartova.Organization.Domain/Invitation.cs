using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain;

public sealed class Invitation : ITenantOwned
{
    private Guid _id;
    public InvitationId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string Email { get; private set; } = "";
    public string Role { get; private set; } = "";
    public Guid InvitedByUserId { get; private set; }
    public DateTimeOffset InvitedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public InvitationStatus Status { get; private set; }
    public Guid? KeycloakUserId { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private Invitation() { }

    public static Invitation Create(
        string email, string role, Guid invitedByUserId,
        Guid keycloakUserId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateEmail(email);
        if (!KartovaRoles.All.Contains(role))
            throw new ArgumentException("Unknown role.", nameof(role));
        var now = clock.GetUtcNow();
        return new Invitation
        {
            _id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            Role = role,
            InvitedByUserId = invitedByUserId,
            InvitedAt = now,
            ExpiresAt = now.AddDays(7),
            Status = InvitationStatus.Pending,
            KeycloakUserId = keycloakUserId,
        };
    }

    public void MarkAccepted(TimeProvider clock)
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot accept invitation in {Status} state.");
        Status = InvitationStatus.Accepted;
        AcceptedAt = clock.GetUtcNow();
    }

    public void Revoke(TimeProvider clock)
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot revoke invitation in {Status} state.");
        Status = InvitationStatus.Revoked;
        RevokedAt = clock.GetUtcNow();
    }

    public void MarkExpired(TimeProvider clock)
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot expire invitation in {Status} state.");
        Status = InvitationStatus.Expired;
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email required.", nameof(email));
        if (email.Length > 320) throw new ArgumentException("Email must be <= 320 characters.", nameof(email));
        if (!email.Contains('@')) throw new ArgumentException("Email must contain '@'.", nameof(email));
    }
}
