using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure.Admin;

public enum AcceptInvitationError { NotFound, GoneExpired, GoneRevoked, GoneAlreadyUsed, Validation, Upstream }

public abstract record GetAcceptContextResult
{
    public sealed record Ok(InvitationAcceptContext Context) : GetAcceptContextResult;
    public sealed record Failed(AcceptInvitationError Error) : GetAcceptContextResult;
}

public abstract record AcceptInvitationResult
{
    public sealed record Ok(string Email) : AcceptInvitationResult;
    public sealed record Failed(AcceptInvitationError Error) : AcceptInvitationResult;
}

/// <summary>
/// Handles the anonymous "accept invitation" flow — resolves the invitation by its
/// globally-unique token hash (across all tenants), finalizes the KeyCloak user,
/// and burns the single-use token.
///
/// Uses <see cref="AdminOrganizationDbContext"/> (BYPASSRLS pool) because token
/// lookup must cross tenant boundaries — the invitee has no tenant context yet.
/// Lives in Infrastructure.Admin for the same reason as
/// <see cref="ExpireInvitationsHostedService"/>: it depends on
/// <see cref="AdminOrganizationDbContext"/> which is defined here.
/// </summary>
public sealed class AcceptInvitationHandler(
    AdminOrganizationDbContext db,
    IKeycloakAdminClient kc,
    TimeProvider clock)
{
    private const int MinPasswordLength = 12;
    private const int MaxPasswordLength = 128;
    private const int MaxDisplayNameLength = 128;

    /// <summary>
    /// Returns the context data required to render the accept-invitation page
    /// (org name, inviter display name, email, role, expiry).
    /// Does NOT mutate state.
    /// </summary>
    public async Task<GetAcceptContextResult> GetContextAsync(string token, CancellationToken ct)
    {
        var (inv, error) = await ResolveAsync(token, ct);
        if (inv is null) return new GetAcceptContextResult.Failed(error!.Value);

        var org = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == inv.TenantId, ct);
        var inviter = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == inv.InvitedByUserId, ct);

        var localPart = inv.Email.Split('@')[0];
        return new GetAcceptContextResult.Ok(new InvitationAcceptContext(
            OrgDisplayName: org?.DisplayName ?? "",
            InvitedByDisplayName: inviter?.DisplayName ?? inv.Email,
            Email: inv.Email,
            DefaultDisplayName: localPart,
            Role: inv.Role,
            ExpiresAt: inv.ExpiresAt));
    }

    /// <summary>
    /// Validates inputs, calls KeyCloak to set the password and finalize the user
    /// (EmailVerified=true, RequiredActions cleared), then burns the invitation token
    /// via <see cref="Invitation.MarkCredentialSet"/> (TokenHash → null, CredentialSetAt
    /// stamped) so the link cannot be replayed.
    /// </summary>
    public async Task<AcceptInvitationResult> AcceptAsync(
        string token, string password, string displayName, CancellationToken ct)
    {
        var (inv, error) = await ResolveAsync(token, ct);
        if (inv is null) return new AcceptInvitationResult.Failed(error!.Value);

        var trimmedName = (displayName ?? "").Trim();
        if (password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength
            || trimmedName.Length is < 1 or > MaxDisplayNameLength)
            return new AcceptInvitationResult.Failed(AcceptInvitationError.Validation);

        var kcId = inv.KeycloakUserId!.Value;
        try
        {
            await kc.SetPasswordAsync(kcId, password, temporary: false, ct);
            await kc.UpdateUserAsync(kcId,
                new UpdateKeycloakUserRequest(trimmedName, null, EmailVerified: true, RequiredActions: Array.Empty<string>()),
                ct);
        }
        catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound)
        {
            // KC user was deleted (e.g. revoked / expired cleanup won a race).
            // The link is effectively gone — treat as already-used.
            return new AcceptInvitationResult.Failed(AcceptInvitationError.GoneAlreadyUsed);
        }

        // Burns the token (TokenHash → null) and stamps CredentialSetAt.
        // Status intentionally stays Pending — it flips to Accepted on first login
        // once the OIDC session is established (spec §6).
        inv.MarkCredentialSet(clock);
        await db.SaveChangesAsync(ct);
        return new AcceptInvitationResult.Ok(inv.Email);
    }

    // Loads the invitation TRACKED (AcceptAsync mutates it). A burned token has
    // TokenHash = null and is therefore simply not found — that is the single-use guard.
    private async Task<(Invitation?, AcceptInvitationError?)> ResolveAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token)) return (null, AcceptInvitationError.NotFound);
        var hash = InvitationToken.Hash(token);
        var inv = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null) return (null, AcceptInvitationError.NotFound);
        return inv.Status switch
        {
            InvitationStatus.Revoked => (null, AcceptInvitationError.GoneRevoked),
            InvitationStatus.Expired => (null, AcceptInvitationError.GoneExpired),
            InvitationStatus.Accepted => (null, AcceptInvitationError.GoneAlreadyUsed),
            InvitationStatus.Pending when inv.ExpiresAt <= clock.GetUtcNow() => (null, AcceptInvitationError.GoneExpired),
            _ => (inv, null),
        };
    }
}
