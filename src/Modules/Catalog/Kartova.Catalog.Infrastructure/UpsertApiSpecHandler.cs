using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Direct-dispatch handler for <see cref="UpsertApiSpecCommand"/> (ADR-0093).
/// Create-or-replace the 1:1 spec row. Tenant/clock/caller from context (ADR-0090). Audit
/// written in-transaction (fail-closed). The delegate has already loaded the API and run the
/// team-membership gate, so this trusts the api id exists in scope.</summary>
public sealed class UpsertApiSpecHandler(TimeProvider clock)
{
    public async Task<bool> Handle(
        UpsertApiSpecCommand cmd, CatalogDbContext db, ITenantContext tenant,
        ICurrentUser user, IAuditWriter audit, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var existing = await db.ApiSpecs.FirstOrDefaultAsync(s => s.ApiId == new ApiId(cmd.ApiId), ct);
        bool created;
        if (existing is null)
        {
            db.ApiSpecs.Add(ApiSpec.Create(
                new ApiId(cmd.ApiId), tenant.Id, cmd.Content, cmd.MediaType, user.UserId, now));
            created = true;
        }
        else
        {
            existing.Replace(cmd.Content, cmd.MediaType);
            created = false;
        }

        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ApiSpecUpdated,
            CatalogAuditTargetTypes.Api,
            cmd.ApiId.ToString(),
            new Dictionary<string, string?>
            {
                ["mediaType"] = cmd.MediaType,
                ["created"] = created ? "true" : "false",
            }), ct);

        return created;
    }
}
