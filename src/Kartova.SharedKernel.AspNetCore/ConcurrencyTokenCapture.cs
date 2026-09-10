using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Shared optimistic-concurrency capture helper. On a <see cref="DbUpdateConcurrencyException"/>,
/// reads the conflicting row's current concurrency-token value and stashes it on
/// <c>ex.Data["currentVersion"]</c> so <see cref="ConcurrencyConflictExceptionHandler"/> can emit a
/// round-trip-saving <c>currentVersion</c> hint on the 412 envelope.
/// <para>
/// The token property is resolved generically from EF Core metadata
/// (<c>IProperty.IsConcurrencyToken</c>) rather than a hard-coded name, so any aggregate whose
/// mapping marks exactly one <see cref="uint"/> concurrency token — the PostgreSQL <c>xmin</c>
/// shape both current callers use — works without a per-entity copy. This replaces the two former
/// near-duplicates keyed by the literal strings <c>"Version"</c> (Application) and <c>"Xmin"</c>
/// (Infrastructure) — TD-002. A token of a different CLR type (<c>byte[]</c> rowversion,
/// <c>Guid</c>, …) resolves the property fine but is skipped by the <c>is uint</c> read below,
/// which logs a warning rather than silently producing no hint.
/// </para>
/// <para>
/// Must run while the tenant connection is still alive — <c>TenantScopeBeginMiddleware</c> rolls
/// back and disposes the connection before <see cref="ConcurrencyConflictExceptionHandler"/> runs,
/// so a fresh read there would fail. Callers invoke this in the handler's <c>catch</c> before
/// rethrowing.
/// </para>
/// </summary>
public static class ConcurrencyTokenCapture
{
    /// <summary>
    /// The <see cref="System.Exception.Data"/> key this helper writes and
    /// <see cref="ConcurrencyConflictExceptionHandler"/> reads — a single shared symbol so a rename
    /// can't silently break the handshake across the two files (TD-002 review).
    /// </summary>
    internal const string CurrentVersionDataKey = "currentVersion";

    /// <summary>
    /// Best-effort capture of the conflicting row's current concurrency-token value onto
    /// <c>ex.Data["currentVersion"]</c>. Swallows and logs any failure — the 412 envelope is still
    /// informative without the hint, and we must not mask the real
    /// <see cref="DbUpdateConcurrencyException"/>. <see cref="OperationCanceledException"/> is
    /// excluded so a request-cancellation mid-recapture isn't silently dropped.
    /// </summary>
    /// <param name="ex">The concurrency exception whose first entry is the conflicting row.</param>
    /// <param name="logger">Logs a warning if capture fails (the only signal that the
    /// connection-lifetime assumption above has broken).</param>
    /// <param name="ct">Cancellation token for the database re-read.</param>
    public static async Task TryCaptureCurrentVersionAsync(
        DbUpdateConcurrencyException ex, ILogger logger, CancellationToken ct)
    {
        try
        {
            var entry = ex.Entries.FirstOrDefault();
            if (entry is null) return;

            var tokenProperty = entry.Metadata.GetProperties()
                .SingleOrDefault(p => p.IsConcurrencyToken);
            if (tokenProperty is null) return;

            var dbValues = await entry.GetDatabaseValuesAsync(ct);
            if (dbValues is null) return;

            var raw = dbValues[tokenProperty.Name];
            if (raw is uint currentVersion)
            {
                ex.Data[CurrentVersionDataKey] = currentVersion;
            }
            else
            {
                // Property resolved, value read, but it isn't the uint xmin shape the 412 hint
                // encodes — don't fall through silently (that's the one gap the catch never sees).
                logger.LogWarning(
                    "Concurrency token '{Property}' was {Type}, not the expected uint; currentVersion hint skipped.",
                    tokenProperty.Name, raw?.GetType().Name ?? "null");
            }
        }
        catch (Exception captureEx) when (captureEx is not OperationCanceledException)
        {
            // Swallow — see summary. Still log: this is the only signal we get if the
            // connection-lifetime assumption above ever breaks.
            logger.LogWarning(
                captureEx,
                "Failed to capture current concurrency token after a concurrency conflict.");
        }
    }
}
