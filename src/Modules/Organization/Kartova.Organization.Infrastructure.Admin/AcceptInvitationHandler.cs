using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kartova.Organization.Infrastructure.Admin;

internal enum AcceptInvitationError { NotFound, GoneExpired, GoneRevoked, GoneAlreadyUsed, Validation, Upstream }

internal abstract record GetAcceptContextResult
{
    public sealed record Ok(InvitationAcceptContext Context) : GetAcceptContextResult;
    public sealed record Failed(AcceptInvitationError Error) : GetAcceptContextResult;
}

internal abstract record AcceptInvitationResult
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
    TimeProvider clock,
    ILogger<AcceptInvitationHandler> logger)
{
    private const int MinPasswordLength = 12;
    private const int MaxPasswordLength = 128;
    private const int MaxDisplayNameLength = 128;

    /// <summary>
    /// Returns the context data required to render the accept-invitation page
    /// (org name, inviter display name, email, role, expiry).
    /// Does NOT mutate state.
    /// </summary>
    internal async Task<GetAcceptContextResult> GetContextAsync(string token, CancellationToken ct)
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
    internal async Task<AcceptInvitationResult> AcceptAsync(
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
            logger.LogWarning(ex, "Accept-invitation: KC user {KcUserId} not found (revoked/expired race); treating as already-used.", kcId);
            return new AcceptInvitationResult.Failed(AcceptInvitationError.GoneAlreadyUsed);
        }
        catch (KeycloakAdminException ex)
        {
            // Any other KC error (network, 5xx, unexpected) — surface as Upstream
            // so the HTTP route can map it to 502 Bad Gateway.
            logger.LogError(ex, "Accept-invitation: KC {KcError} finalizing user {KcUserId}; surfacing as Upstream.", ex.Error, kcId);
            return new AcceptInvitationResult.Failed(AcceptInvitationError.Upstream);
        }

        // Burns the token (TokenHash → null) and stamps CredentialSetAt so the link
        // cannot be replayed: a re-submitted token resolves to not-found (null hash ≠ any stored hash).
        // TokenHash is a concurrency token (IsConcurrencyToken in entity config): EF appends
        // "AND token_hash = <original>" to the UPDATE WHERE clause. Two concurrent accepts of
        // the same token both pass ResolveAsync, but only one can commit — the loser's row
        // will have a different (already-null) token_hash and EF throws DbUpdateConcurrencyException.
        // No migration needed — IsConcurrencyToken is client-side metadata; token_hash already exists.
        // Status intentionally stays Pending — it flips to Accepted on first login
        // once the OIDC session is established (spec §6).
        inv.MarkCredentialSet(clock);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent accept of the same token won the race and burned it first.
            return new AcceptInvitationResult.Failed(AcceptInvitationError.GoneAlreadyUsed);
        }
        return new AcceptInvitationResult.Ok(inv.Email);
    }

    // Loads the invitation TRACKED (AcceptAsync mutates it). A burned token has
    // TokenHash = null and resolves to not-found — that is the single-use enforcement mechanism.
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
