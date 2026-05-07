using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

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
            // Exception.Data is the handoff path.
            await TryCaptureCurrentVersionAsync(ex, ct);
            throw;
        }

        return app.ToResponse();
    }

    private static async Task TryCaptureCurrentVersionAsync(
        DbUpdateConcurrencyException ex, CancellationToken ct)
    {
        try
        {
            var entry = ex.Entries.FirstOrDefault();
            if (entry is null) return;

            var dbValues = await entry.GetDatabaseValuesAsync(ct);
            if (dbValues is null) return;

            if (dbValues["Version"] is uint currentVersion)
            {
                ex.Data["currentVersion"] = currentVersion;
            }
        }
        catch (Exception captureEx) when (captureEx is not OperationCanceledException)
        {
            // Swallow — the 412 envelope is still informative without the
            // currentVersion extension; we just lose the round-trip-saving
            // hint. Don't mask the real DbUpdateConcurrencyException.
            // OperationCanceledException is excluded so a request-cancellation
            // mid-recapture isn't silently dropped.
        }
    }
}
