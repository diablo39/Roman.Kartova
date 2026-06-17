using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Applies an <see cref="UpdateOrgProfileRequest"/> to the current tenant's
/// <c>Organization</c> aggregate (slice-9 spec §4). Domain invariants
/// (display name length, description length, IANA timezone) are enforced by
/// <c>Organization.UpdateProfile</c>; the resulting <see cref="ArgumentException"/>
/// is caught by <c>DomainValidationExceptionHandler</c> upstream and rendered
/// as RFC 7807 400 — handlers deliberately do not catch it.
/// </summary>
public sealed class UpdateOrgProfileHandler
{
    private readonly OrganizationDbContext _db;
    private readonly IAuditWriter _audit;

    public UpdateOrgProfileHandler(OrganizationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style", "IDE0060:Remove unused parameter",
        Justification = "ifMatch is reserved on the wire contract per slice-9 spec §4 + ADR-0096. The Organization aggregate does not carry an EF concurrency token yet, so this argument is currently unused. Both the delegate (which passes null) and this handler will be wired to IfMatchEndpointFilter when xmin mapping lands.")]
    public async Task<UpdateOrgProfileResult> HandleAsync(
        UpdateOrgProfileRequest request,
        byte[]? ifMatch,
        CancellationToken ct)
    {
        var org = await _db.Organizations.FirstOrDefaultAsync(ct);
        if (org is null) return UpdateOrgProfileResult.NotFound;

        org.UpdateProfile(request.DisplayName, request.Description, request.DefaultTimeZone);

        // Optimistic concurrency wire contract reserved per slice-9 spec §4. The
        // Organization aggregate does not carry an EF concurrency token yet, so this
        // catch is currently unreachable — a follow-up will add xmin mapping without
        // changing the handler/endpoint surface.
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UpdateOrgProfileResult.ConcurrencyConflict;
        }
        await _audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.OrgProfileUpdated,
            AuditTargetTypes.Organization,
            org.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = request.DisplayName,
                ["defaultTimeZone"] = request.DefaultTimeZone,
            }), ct);
        return UpdateOrgProfileResult.Ok;
    }
}

/// <summary>
/// Outcome of <see cref="UpdateOrgProfileHandler.HandleAsync"/>. Lives in the
/// same file as the handler — the slice-8 convention (e.g.
/// <c>AddTeamMemberResult</c>) keeps tightly-coupled result enums next to
/// their owning handler.
/// </summary>
public enum UpdateOrgProfileResult
{
    Ok,
    NotFound,
    ConcurrencyConflict,
}
