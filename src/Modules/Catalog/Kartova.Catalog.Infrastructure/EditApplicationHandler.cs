using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="EditApplicationCommand"/>. Returns
/// <c>null</c> when no row is visible in the current tenant scope (RLS
/// auto-filters cross-tenant rows — handler does not need an explicit tenant
/// id). Concurrency: sets <c>OriginalValue(Version)</c> to the supplied
/// <c>ExpectedVersion</c> so EF's UPDATE includes <c>WHERE xmin = :expected</c>;
/// mismatch raises <see cref="DbUpdateConcurrencyException"/> → 412.
/// </summary>
public sealed class EditApplicationHandler
{
    public async Task<ApplicationResponse?> Handle(
        EditApplicationCommand cmd,
        CatalogDbContext db,
        IAuditWriter audit,
        ILogger<EditApplicationHandler> logger,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        db.Entry(app).Property(a => a.Version).OriginalValue = cmd.ExpectedVersion;

        app.EditMetadata(cmd.DisplayName, cmd.Description);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Capture the current row version while the tenant connection is
            // still alive — TenantScopeBeginMiddleware rolls back and disposes
            // the connection before ConcurrencyConflictExceptionHandler runs,
            // so a fresh GetDatabaseValuesAsync would fail there. Stashing on
            // Exception.Data is the handoff path. Shared metadata-driven capture
            // (TD-002) — resolves the token property from EF metadata, no hard-coded "Version".
            await ConcurrencyTokenCapture.TryCaptureCurrentVersionAsync(ex, logger, ct);
            throw;
        }

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ApplicationEdited,
            CatalogAuditTargetTypes.Application,
            app.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = app.DisplayName,
                ["description"] = app.Description,
            }), ct);

        return app.ToResponse();
    }
}
