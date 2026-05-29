using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Creates a new invitation: validates input, enforces the three-way email
/// conflict model (already-in-tenant / already-invited / already-on-platform),
/// creates the KeyCloak user, assigns the realm role, then persists the
/// Invitation + User-projection rows in a single SaveChangesAsync. Spec §6.7.
///
/// <para>
/// Compensation: if role assignment fails after the KC user was created, the
/// KC user is best-effort deleted. DB failure after a successful KC create+role
/// is currently NOT compensated — see slice-9 spec §6.7 follow-up notes.
/// </para>
/// </summary>
public sealed class CreateInvitationHandler(
    OrganizationDbContext db,
    IKeycloakAdminClient kc,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<KeycloakAdminOptions> options)
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
        catch (KeycloakAdminException)
        {
            // Best-effort compensation: if we orphan the KC user, the platform
            // shows an unreachable email at the realm level. Swallow secondary
            // failures so the original 502 is still the surfaced reason.
            try { await kc.DeleteUserAsync(kcId, ct); }
#pragma warning disable CA1031 // intentional best-effort swallow per spec §6.7
            catch { }
#pragma warning restore CA1031
            return new CreateInvitationResult.Failed(CreateInvitationError.Upstream);
        }

        var invitation = Invitation.Create(email, request.Role, currentUser.UserId, kcId, tenant.Id, clock);
        db.Invitations.Add(invitation);
        db.Users.Add(new User
        {
            Id = kcId,
            TenantId = tenant.Id,
            Email = email,
            DisplayName = email,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);

        // Spec §9.2 step 8: the URL carries the sentinel `?invitation=1` flag
        // rather than a per-invitation token — acceptance is keyed off the
        // authenticated user's email at the OIDC callback (§9.3 step 6) so a
        // token is intentionally unnecessary. The `email` query parameter is
        // a UX hint: the invitee can see at a glance which email to log in
        // with, and a future SPA change can lift it into Keycloak's
        // `login_hint` parameter on the OIDC redirect without a contract
        // change. Documented as the resolution of H4 API-1 in
        // docs/superpowers/plans/slice-9-docker-verification.md.
        var inviteUrl =
            $"{options.Value.FrontendBaseUrl}/?invitation=1&email={Uri.EscapeDataString(email)}";
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
