namespace Kartova.Audit.Domain;

/// <summary>
/// Outcome of walking a tenant's audit chain. <see cref="Intact"/> rows verify end-to-end;
/// otherwise <see cref="FirstBrokenSeq"/> + <see cref="Reason"/> describe the first break.
/// </summary>
public sealed record AuditChainVerificationResult(bool Intact, long? FirstBrokenSeq, string? Reason)
{
    public static AuditChainVerificationResult Ok { get; } = new(true, null, null);
    public static AuditChainVerificationResult Broken(long seq, string reason) => new(false, seq, reason);
}
