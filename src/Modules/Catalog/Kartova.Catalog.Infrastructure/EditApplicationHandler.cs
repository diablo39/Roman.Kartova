using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="EditApplicationCommand"/>. Mirrors
/// the GetApplicationByIdHandler nullable-return pattern: null = not found in
/// current tenant scope (RLS auto-filters cross-tenant rows). Endpoint
/// delegate maps null to RFC 7807 404.
///
/// Concurrency: handler sets <c>OriginalValue(Version)</c> to the supplied
/// ExpectedVersion so EF's UPDATE includes <c>WHERE xmin = :expected</c>;
/// mismatch raises DbUpdateConcurrencyException → 412
/// (ConcurrencyConflictExceptionHandler).
/// </summary>
public sealed class EditApplicationHandler
{
    public async Task<ApplicationResponse?> Handle(
        EditApplicationCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
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
            // Capture the current row version on the still-alive tenant connection
            // BEFORE the exception escapes the handler. Once the exception bubbles
            // up the pipeline, TenantScopeBeginMiddleware's finally block will roll
            // back and dispose the connection — at which point GetDatabaseValuesAsync
            // would fail. Stash on Exception.Data so ConcurrencyConflictExceptionHandler
            // can read it without re-querying the DB.
            await TryCaptureCurrentVersionAsync(ex, ct);
            throw;
        }

        return app.ToResponse();
    }

    /// <summary>
    /// Best-effort fetch of the row's current xmin via EF's tracked entry. Failures
    /// are swallowed so the original concurrency exception still surfaces.
    /// </summary>
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
        catch
        {
            // Connection or DbContext may be disposed; the conflict status itself is
            // still informative without the extension. Don't mask the real exception.
        }
    }
}
