using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Creates a new invitation: validates input, enforces the three-way email
/// conflict model (already-in-tenant / already-invited / already-on-platform),
/// creates the KeyCloak user, assigns the realm role, then persists the
/// Invitation + User-projection rows in a single SaveChangesAsync. Spec §6.7.
///
/// <para>
/// Compensation: if role assignment fails after the KC user was created, the
/// KC user is best-effort deleted. DB persistence failure after a successful
/// KC create+role is also best-effort compensated: the handler attempts a
/// <c>kc.DeleteUserAsync</c> and propagates the original <c>DbUpdateException</c>.
/// An orphan KC user therefore requires BOTH the DB write AND the compensation
/// delete to fail — a far narrower failure mode than the prior gap.
/// </para>
/// </summary>
public sealed class CreateInvitationHandler(
    OrganizationDbContext db,
    IKeycloakAdminClient kc,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<KeycloakAdminOptions> options,
    ILogger<CreateInvitationHandler> logger)
{
    public async Task<CreateInvitationResult> HandleAsync(CreateInvitationRequest request, CancellationToken ct)
    {
        // Normalize email up front: trim + lowercase so duplicate checks are
        // case-insensitive (matches the storage form set by Invitation.Create).
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 320)
        {
            return new CreateInvitationResult.Failed(CreateInvitationError.Validation);
        }

        // Defensive role allow-list — Invitation.Create also enforces this, but
        // failing here avoids spending a KeyCloak round-trip on a bad role.
        if (!KartovaRoles.All.Contains(request.Role))
        {
            return new CreateInvitationResult.Failed(CreateInvitationError.Validation);
        }

        // Three-way conflict guards (spec §6.7 — preflight rejection before KC):
        //   • email already a User row in this tenant → already accepted prior invite.
        //   • email already a Pending Invitation in this tenant → duplicate invite.
        // The KeyCloak EmailAlreadyExists path handles the platform-wide case.
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return new CreateInvitationResult.Failed(CreateInvitationError.EmailAlreadyInTenant);
        }

        var existingPending = await db.Invitations
            .FirstOrDefaultAsync(i => i.Email == email && i.Status == InvitationStatus.Pending, ct);
        if (existingPending is not null)
        {
            return new CreateInvitationResult.Failed(CreateInvitationError.EmailAlreadyInvited);
        }

        Guid kcId;
        try
        {
            kcId = await kc.CreateUserAsync(new CreateKeycloakUserRequest(
                email, null, null, tenant.Id.Value.ToString(),
                new[] { KeycloakAdminRequiredActions.UpdatePassword }), ct);
        }
        catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.EmailAlreadyExists)
        {
            return new CreateInvitationResult.Failed(CreateInvitationError.EmailAlreadyOnPlatform);
        }

        try
        {
            await kc.AssignRealmRoleAsync(kcId, request.Role, ct);
        }
        catch (KeycloakAdminException ex)
        {
            // Best-effort compensation: if we orphan the KC user, the platform
            // shows an unreachable email at the realm level. Swallow secondary
            // failures so the original 502 is still the surfaced reason.
            logger.LogError(
                ex,
                "Invitation creation failed: KC AssignRealmRoleAsync threw for {KcUserId}; attempting compensation delete.",
                kcId);
            try { await kc.DeleteUserAsync(kcId, ct); }
#pragma warning disable CA1031 // intentional best-effort swallow per spec §6.7
            catch (Exception cleanupEx)
            {
                logger.LogWarning(
                    cleanupEx,
                    "Compensation delete of orphaned KC user {KcUserId} also failed after role-assign error.",
                    kcId);
            }
#pragma warning restore CA1031
            return new CreateInvitationResult.Failed(CreateInvitationError.Upstream);
        }

        var (tokenPlaintext, tokenHash) = InvitationToken.Issue();
        var invitation = Invitation.Create(email, request.Role, currentUser.UserId, kcId, tenant.Id, clock, tokenHash);
        db.Invitations.Add(invitation);
        db.Users.Add(new User
        {
            Id = kcId,
            TenantId = tenant.Id,
            Email = email,
            DisplayName = email,
            CreatedAt = clock.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            // Race-condition path closed by carry-forward #10 (migration
            // MakeInvitationsPendingIndexUnique): the partial UNIQUE index
            // idx_invitations_email_pending caught a concurrent invite that
            // slipped past the AnyAsync pre-check above. Translate the
            // PostgreSQL unique-violation into the same outcome the
            // application-level pre-check produces, then best-effort delete
            // the orphaned KC user so the realm doesn't carry an unreachable
            // shadow account (same compensation pattern as the role-assign
            // failure branch above).
            logger.LogError(
                ex,
                "Invitation creation lost concurrent race (23505 unique-violation) for {KcUserId} email {Email}; attempting compensation delete.",
                kcId, email);
            try { await kc.DeleteUserAsync(kcId, ct); }
#pragma warning disable CA1031 // intentional best-effort swallow per spec §6.7
            catch (Exception cleanupEx)
            {
                logger.LogWarning(
                    cleanupEx,
                    "Compensation delete of orphaned KC user {KcUserId} also failed after 23505 race.",
                    kcId);
            }
#pragma warning restore CA1031
            return new CreateInvitationResult.Failed(CreateInvitationError.EmailAlreadyInvited);
        }
        catch (DbUpdateException ex)
        {
            // Any other DB persistence failure (FK violation, connection loss,
            // timeout, etc.) leaves a KC user provisioned but no DB invitation
            // row. Best-effort compensation: delete the orphan KC user so the
            // realm doesn't carry an unreachable shadow account, then propagate
            // the original DbUpdateException so the caller sees the real cause.
            // Same compensation pattern as the role-assign branch above + the
            // 23505 branch immediately above this one. An orphan now requires
            // BOTH the SaveChangesAsync AND the kc.DeleteUserAsync to fail —
            // significantly narrower than the prior unmitigated gap.
            logger.LogError(
                ex,
                "Invitation DB persistence failed AFTER KC create+role for {KcUserId}; attempting compensation.",
                kcId);
            try { await kc.DeleteUserAsync(kcId, ct); }
#pragma warning disable CA1031 // intentional best-effort swallow per spec §6.7
            catch (Exception cleanupEx)
            {
                logger.LogWarning(
                    cleanupEx,
                    "Compensation delete of orphaned KC user {KcUserId} also failed.",
                    kcId);
            }
#pragma warning restore CA1031
            throw;
        }

        // The URL carries an opaque single-use token; only its SHA-256 hash is
        // persisted (InvitationToken). The plaintext is returned once here and
        // never logged — the invitee exchanges it via the accept-invitation flow.
        var inviteUrl =
            $"{options.Value.FrontendBaseUrl}/accept-invitation?token={Uri.EscapeDataString(tokenPlaintext)}";
        var response = new InvitationResponse(
            invitation.Id.Value,
            invitation.Email,
            invitation.Role,
            invitation.InvitedAt,
            invitation.ExpiresAt,
            invitation.Status.ToString(),
            invitation.InvitedByUserId,
            invitation.AcceptedAt,
            invitation.RevokedAt);
        return new CreateInvitationResult.Created(new CreateInvitationResponse(response, inviteUrl));
    }
}

/// <summary>
/// Discriminated outcome for <see cref="CreateInvitationHandler.HandleAsync"/>.
/// Co-located with the handler since the failure taxonomy is part of the
/// handler's contract (spec §6.7 three-way conflict model).
/// </summary>
public abstract record CreateInvitationResult
{
    public sealed record Created(CreateInvitationResponse Response) : CreateInvitationResult;
    public sealed record Failed(CreateInvitationError Error) : CreateInvitationResult;
}

public enum CreateInvitationError
{
    Validation,
    EmailAlreadyInTenant,
    EmailAlreadyInvited,
    EmailAlreadyOnPlatform,
    Upstream,
}
