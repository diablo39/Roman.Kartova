using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="EditEnvironmentCommand"/> (A2). Returns
/// <c>null</c> when no row is visible in the current tenant scope (RLS auto-filters
/// cross-tenant rows). Concurrency: sets <c>OriginalValue(Xmin)</c> to the supplied
/// <c>ExpectedVersion</c> so EF's UPDATE includes <c>WHERE xmin = :expected</c>; mismatch
/// raises <see cref="DbUpdateConcurrencyException"/> → 412. Mirrors <see cref="EditVmHandler"/>.
/// </summary>
public sealed class EditEnvironmentHandler
{
    public async Task<EnvironmentDetailResponse?> Handle(
        EditEnvironmentCommand cmd, CatalogDbContext db, IAuditWriter audit, ILogger<EditEnvironmentHandler> logger, CancellationToken ct)
    {
        var env = await db.Environments
            .SingleOrDefaultAsync(EnvironmentSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (env is null) return null;

        db.Entry(env).Property(x => x.Xmin).OriginalValue = cmd.ExpectedVersion;

        env.Edit(cmd.DisplayName, cmd.Description, cmd.Region, cmd.ResourceDetailsJson);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Shared metadata-driven capture (TD-002) — resolves the concurrency-token
            // property from EF metadata, no hard-coded "Xmin".
            await ConcurrencyTokenCapture.TryCaptureCurrentVersionAsync(ex, logger, ct);
            throw;
        }

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.EnvironmentEdited,
            CatalogAuditTargetTypes.Environment,
            env.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = env.DisplayName,
                ["type"] = env.Type.ToString(),
            }), ct);

        return new EnvironmentDetailResponse(
            env.Id.Value, env.TenantId.Value, env.DisplayName, env.Description, env.Type, env.Region,
            EnvironmentResourceDetails.FromJson(env.ResourceDetails), env.CreatedByUserId, env.CreatedAt,
            VersionEncoding.Encode(env.Xmin));
    }
}
