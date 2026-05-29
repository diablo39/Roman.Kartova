using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Revokes a pending invitation: deletes the linked KeyCloak user (best-effort —
/// swallows NotFound in case the user was already cleaned up), removes the
/// matching <c>users</c> projection row that <see cref="CreateInvitationHandler"/>
/// inserted alongside the invitation, and transitions the aggregate to
/// <c>Revoked</c>. Spec §6.7.
///
/// <para>
/// Re-invite contract: removing the projection row is required to let an
/// OrgAdmin re-invite the same email after a revoke (e.g. fat-finger correction)
/// — the create-handler's "email already in this tenant" pre-check
/// (<see cref="CreateInvitationHandler"/>) would otherwise 409 forever on
/// the orphan row. Safe to delete unconditionally inside this handler because
/// the status guard above short-circuits on anything other than Pending, so
/// we only ever drop projection stubs that came from the unaccepted invite.
/// </para>
/// </summary>
public sealed class RevokeInvitationHandler(
    OrganizationDbContext db,
    IKeycloakAdminClient kc,
    TimeProvider clock)
{
    public async Task<RevokeResult> HandleAsync(Guid invitationId, CancellationToken ct)
    {
        var inv = await db.Invitations.FirstOrDefaultAsync(
            InvitationSortSpecs.IdEquals(invitationId), ct);
        if (inv is null) return RevokeResult.NotFound;
        if (inv.Status != Domain.InvitationStatus.Pending) return RevokeResult.NotPending;

        try
        {
            if (inv.KeycloakUserId is { } kid)
            {
                await kc.DeleteUserAsync(kid, ct);
            }
        }
        catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound)
        {
            // Idempotent cleanup: a missing KC user is the desired end state.
        }
        catch (KeycloakAdminException)
        {
            // KC reachable but rejecting the delete (Unauthorized / Unexpected).
            // Don't partially commit: leave the invitation Pending so a retry can
            // complete the revoke cleanly. Returns 502 via the endpoint result-
            // switch — matches CreateInvitationHandler's Upstream wire contract on
            // the same KC failure class, so clients see a consistent surface
            // across both endpoints.
            return RevokeResult.Upstream;
        }

        // Drop the users-projection row that CreateInvitationHandler inserted
        // (keyed by the same KC user id that the invitation captured). The
        // Pending-status guard above ensures this row is still the projection
        // stub — once the invitee logs in, the post-auth claims sync upserts
        // the row with real data AND flips the invitation to Accepted, at
        // which point this handler short-circuits at NotPending and never
        // reaches this delete. Without this step, a revoke leaves a dangling
        // users row that blocks every future invite for the same email with
        // a 409 EmailAlreadyInTenant — see review finding #1.
        if (inv.KeycloakUserId is { } projectionId)
        {
            var userRow = await db.Users.FirstOrDefaultAsync(u => u.Id == projectionId, ct);
            if (userRow is not null)
            {
                db.Users.Remove(userRow);
            }
        }

        inv.Revoke(clock);
        await db.SaveChangesAsync(ct);
        return RevokeResult.Ok;
    }
}

public enum RevokeResult { Ok, NotFound, NotPending, Upstream }
