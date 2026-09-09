using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Shared optimistic-concurrency capture helper for the Infrastructure/VM write handlers
/// (<see cref="EditVmHandler"/>, <see cref="DeleteVmHandler"/> — slice 2a Tasks 5/6). Extracted
/// from <c>EditVmHandler.TryCaptureCurrentXminAsync</c> (Task 5) once a second caller appeared,
/// per the DRY requirement — behavior is byte-identical to the original private method.
/// </summary>
internal static class InfrastructureConcurrency
{
    /// <summary>
    /// Best-effort read of the row's current <c>Xmin</c> after a
    /// <see cref="DbUpdateConcurrencyException"/>, stashed on <c>ex.Data["currentVersion"]</c>
    /// for the 412 envelope's round-trip-saving hint. Reads
    /// <c>entry.GetDatabaseValuesAsync</c> while the tenant connection is still alive —
    /// <c>TenantScopeBeginMiddleware</c> rolls back and disposes the connection before
    /// <c>ConcurrencyConflictExceptionHandler</c> runs, so a fresh read there would fail.
    /// Swallows failures: the 412 envelope is still informative without the extension, and we
    /// must not mask the real <see cref="DbUpdateConcurrencyException"/>.
    /// <see cref="OperationCanceledException"/> is excluded so a request-cancellation
    /// mid-recapture isn't silently dropped.
    /// </summary>
    internal static async Task TryCaptureCurrentXminAsync(
        DbUpdateConcurrencyException ex, CancellationToken ct)
    {
        try
        {
            var entry = ex.Entries.FirstOrDefault();
            if (entry is null) return;

            var dbValues = await entry.GetDatabaseValuesAsync(ct);
            if (dbValues is null) return;

            if (dbValues["Xmin"] is uint currentVersion)
            {
                ex.Data["currentVersion"] = currentVersion;
            }
        }
        catch (Exception captureEx) when (captureEx is not OperationCanceledException)
        {
            // Swallow — see summary.
        }
    }
}
