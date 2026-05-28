using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Revokes a pending invitation: deletes the linked KeyCloak user (best-effort —
/// swallows NotFound in case the user was already cleaned up) and transitions
/// the aggregate to <c>Revoked</c>. Spec §6.7.
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

        inv.Revoke(clock);
        await db.SaveChangesAsync(ct);
        return RevokeResult.Ok;
    }
}

public enum RevokeResult { Ok, NotFound, NotPending }
