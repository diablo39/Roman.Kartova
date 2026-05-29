using System.Security.Claims;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Organization-module post-auth hook (spec §4.3, §5.2). Runs inside
/// <c>TenantScopeBeginMiddleware</c> AFTER <see cref="ITenantScope"/> is active
/// (and AFTER <see cref="TenantClaimsTransformation"/> has populated the tenant +
/// role claims and the membership reader has filled team memberships) and:
/// <list type="number">
///   <item>upserts the <c>users</c> projection from JWT claims (sub/email/given_name/family_name);</item>
///   <item>if a <see cref="Invitation"/> exists for the caller's KeyCloak user id in
///   <see cref="InvitationStatus.Pending"/> and is not expired, calls
///   <see cref="Invitation.MarkAccepted"/> and surfaces the invitation id via
///   <see cref="ITenantContext.SetJustAcceptedInvitation"/> so handlers in the SAME
///   request can react to it.</item>
/// </list>
/// Idempotent: the upsert merges by user id and the pending-status guard prevents
/// double-acceptance if the hook runs more than once per request.
/// Consumed only via <see cref="IPostAuthSyncHook"/> DI — the concrete type is
/// internal by design.
/// </summary>
internal sealed class OrganizationPostAuthSyncHook(
    OrganizationDbContext db,
    UserProjectionUpdater projection,
    ITenantContext tenantContext,
    TimeProvider clock) : IPostAuthSyncHook
{
    public async Task ExecuteAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // Claim names follow OpenID Connect Core §5.1 ("sub", "email", "given_name",
        // "family_name") — they are the wire contract from KC's ID/access tokens, not
        // magic strings. We check both the raw OIDC form AND the .NET ClaimTypes.*
        // form because some pipeline stages (notably DefaultClaimsTransformation)
        // rewrite "sub" → ClaimTypes.NameIdentifier and "email" → ClaimTypes.Email
        // before the principal reaches us. Production typically sees only the OIDC
        // form (JwtBearer with MapInboundClaims=false), but the dual lookup keeps
        // the hook robust to upstream claim-transformation changes.
        var subRaw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? principal.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subRaw, out var userId)) return;
        if (!tenantContext.IsTenantScoped) return;
        var tenantId = tenantContext.Id;

        var email = principal.FindFirst("email")?.Value
                 ?? principal.FindFirst(ClaimTypes.Email)?.Value
                 ?? "";
        var given = principal.FindFirst("given_name")?.Value;
        var family = principal.FindFirst("family_name")?.Value;

        if (string.IsNullOrWhiteSpace(email)) return;   // can't materialize a row

        await projection.UpsertAsync(db, userId, email, given, family, tenantId, ct);

        // Invitation-acceptance side effect — only flips Pending → Accepted when the
        // invitation has not yet expired (the background expirer covers stale rows).
        var pending = await db.Invitations
            .FirstOrDefaultAsync(i => i.KeycloakUserId == userId && i.Status == InvitationStatus.Pending, ct);
        if (pending is not null && pending.ExpiresAt > clock.GetUtcNow())
        {
            pending.MarkAccepted(clock);
            await db.SaveChangesAsync(ct);
            tenantContext.SetJustAcceptedInvitation(pending.Id.Value);
        }
    }
}
