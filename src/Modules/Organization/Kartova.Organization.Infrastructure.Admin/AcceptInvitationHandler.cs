using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        // Validate inputs up front — cheap, independent of the invitation, and avoids
        // holding the serialization lock (below) while rejecting a malformed request.
        var trimmedName = (displayName ?? "").Trim();
        if (password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength
            || trimmedName.Length is < 1 or > MaxDisplayNameLength)
            return new AcceptInvitationResult.Failed(AcceptInvitationError.Validation);

        if (string.IsNullOrEmpty(token))
            return new AcceptInvitationResult.Failed(AcceptInvitationError.NotFound);
        var hash = InvitationToken.Hash(token);

        // Serialize concurrent accepts of the SAME token so only ONE request determines the
        // winner and calls KeyCloak. Without this, two concurrent requests both pass the
        // status check and both call kc.SetPassword/UpdateUser BEFORE the token is burned —
        // and KeyCloak, hit twice for the same user, can transiently error on one (surfaced
        // as 502) instead of the loser being cleanly rejected. The advisory xact-lock (same
        // idiom as AuditWriter) is held for the request's transaction and released on
        // commit/rollback: the loser blocks until the winner commits the burn (token_hash →
        // null), then re-reads, finds no matching row, and returns NotFound (404) WITHOUT
        // calling KeyCloak. The burn deliberately stays AFTER the KeyCloak calls, so a real
        // KeyCloak failure rolls the transaction back and leaves the token intact for retry.
        //
        // Relational only: the unit tests run on the EF InMemory provider, which has no
        // advisory locks or transactions. They cover the non-concurrent logic branches; the
        // real-Postgres integration test exercises the serialization.
        IDbContextTransaction? tx = null;
        if (db.Database.IsRelational())
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext('kartova.invitation-accept'), hashtext({hash}))", ct);
        }
        await using var _txScope = tx;

        var inv = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        var error = Evaluate(inv);
        if (error is not null) return new AcceptInvitationResult.Failed(error.Value);

        var kcId = inv!.KeycloakUserId!.Value;
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
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Defensive: under the advisory lock a concurrent same-token accept cannot reach
            // here, but the token_hash concurrency token still guards against any other writer
            // (e.g. a revoke) that burned/changed the row. Disposing the (uncommitted)
            // transaction rolls back.
            return new AcceptInvitationResult.Failed(AcceptInvitationError.GoneAlreadyUsed);
        }
        return new AcceptInvitationResult.Ok(inv.Email);
    }

    // Maps a (possibly null / non-Pending) invitation to the matching failure, or null when
    // it is acceptable. Shared by ResolveAsync (read path) and AcceptAsync (locked write path).
    private AcceptInvitationError? Evaluate(Invitation? inv) =>
        inv is null
            ? AcceptInvitationError.NotFound
            : inv.Status switch
            {
                InvitationStatus.Revoked => AcceptInvitationError.GoneRevoked,
                InvitationStatus.Expired => AcceptInvitationError.GoneExpired,
                InvitationStatus.Accepted => AcceptInvitationError.GoneAlreadyUsed,
                InvitationStatus.Pending when inv.ExpiresAt <= clock.GetUtcNow() => AcceptInvitationError.GoneExpired,
                _ => null,
            };

    // Loads the invitation TRACKED (AcceptAsync mutates it). A burned token has
    // TokenHash = null and resolves to not-found — that is the single-use enforcement mechanism.
    private async Task<(Invitation?, AcceptInvitationError?)> ResolveAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token)) return (null, AcceptInvitationError.NotFound);
        var hash = InvitationToken.Hash(token);
        var inv = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        var error = Evaluate(inv);
        return error is null ? (inv, null) : (null, error);
    }
}
