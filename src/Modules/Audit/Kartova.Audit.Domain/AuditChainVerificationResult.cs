namespace Kartova.Audit.Domain;

/// <summary>
/// Outcome of walking a tenant's audit chain. <see cref="Intact"/> is derived from
/// <see cref="FirstBrokenSeq"/> so the two can never disagree: a result is intact iff no break
/// was recorded. Construct only via <see cref="Ok"/> / <see cref="Broken"/> — the constructor is
/// private so an incoherent (intact + broken-seq) state cannot be created.
/// </summary>
public sealed record AuditChainVerificationResult
{
    private AuditChainVerificationResult(long? firstBrokenSeq, string? reason)
    {
        FirstBrokenSeq = firstBrokenSeq;
        Reason = reason;
    }

    /// <summary>True iff no break was found (i.e. <see cref="FirstBrokenSeq"/> is null).</summary>
    public bool Intact => FirstBrokenSeq is null;

    public long? FirstBrokenSeq { get; }

    public string? Reason { get; }

    public static AuditChainVerificationResult Ok { get; } = new(null, null);

    public static AuditChainVerificationResult Broken(long seq, string reason) => new(seq, reason);
}
