using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="DeleteEnvironmentCommand"/> — hard delete (A2).
/// Returns <see langword="false"/> when no row is visible in the current tenant scope
/// (RLS auto-filters cross-tenant rows, so an unknown id and a cross-tenant id both surface
/// as "not found" — mirrors <see cref="EditEnvironmentHandler"/>). Concurrency: sets
/// <c>OriginalValue(Xmin)</c> to the supplied <c>ExpectedVersion</c> so EF's generated
/// <c>DELETE ... WHERE xmin = :expected</c> raises <see cref="DbUpdateConcurrencyException"/>
/// on a stale version (mapped to 412 upstream, never 409). No lifecycle and no relationship
/// edges reference Environment yet, so this is a plain <c>db.Environments.Remove</c> — no
/// soft-delete, no cascading cleanup. Mirrors <see cref="DeleteVmHandler"/>.
/// </summary>
public sealed class DeleteEnvironmentHandler
{
    public async Task<bool> Handle(
        DeleteEnvironmentCommand cmd, CatalogDbContext db, IAuditWriter audit, ILogger<DeleteEnvironmentHandler> logger, CancellationToken ct)
    {
        var env = await db.Environments
            .SingleOrDefaultAsync(EnvironmentSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (env is null) return false;

        db.Entry(env).Property(x => x.Xmin).OriginalValue = cmd.ExpectedVersion;
        db.Environments.Remove(env);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await ConcurrencyTokenCapture.TryCaptureCurrentVersionAsync(ex, logger, ct);
            throw;
        }

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.EnvironmentDeleted,
            CatalogAuditTargetTypes.Environment,
            env.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = env.DisplayName,
                ["type"] = env.Type.ToString(),
            }), ct);

        return true;
    }
}
