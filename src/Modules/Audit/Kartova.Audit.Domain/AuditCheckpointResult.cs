namespace Kartova.Audit.Domain;

/// <summary>Outcome of an attempt to create an audit-chain checkpoint (ADR-0105).</summary>
public enum AuditCheckpointOutcome
{
    /// <summary>A new checkpoint was written over a newly verified tail.</summary>
    Created,

    /// <summary>The chain had no rows beyond the latest checkpoint — nothing new to attest.</summary>
    UpToDate,

    /// <summary>The tail failed verification; no checkpoint was written.</summary>
    ChainBroken,
}

/// <summary>
/// Result of <c>AuditCheckpointer.CreateAsync</c>. A checkpoint is only ever written over a
/// verified-intact prefix, so a <see cref="AuditCheckpointOutcome.ChainBroken"/> outcome carries the
/// failing <see cref="Verification"/> and leaves <see cref="Checkpoint"/> null. The fields are
/// derived from <see cref="Outcome"/> via the factories so an incoherent state cannot be built.
/// </summary>
public sealed record AuditCheckpointResult
{
    private AuditCheckpointResult(AuditCheckpointOutcome outcome, AuditCheckpoint? checkpoint, AuditChainVerificationResult verification)
    {
        Outcome = outcome;
        Checkpoint = checkpoint;
        Verification = verification;
    }

    public AuditCheckpointOutcome Outcome { get; }

    /// <summary>The newly written checkpoint (Created) or the existing latest one (UpToDate); null when broken or none exists.</summary>
    public AuditCheckpoint? Checkpoint { get; }

    /// <summary>The tail verification result: <see cref="AuditChainVerificationResult.Ok"/> unless the outcome is <see cref="AuditCheckpointOutcome.ChainBroken"/>.</summary>
    public AuditChainVerificationResult Verification { get; }

    public static AuditCheckpointResult Created(AuditCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(AuditCheckpointOutcome.Created, checkpoint, AuditChainVerificationResult.Ok);
    }

    /// <summary>
    /// The chain is current — nothing new to attest. <paramref name="latest"/> is the existing head
    /// checkpoint, or null when the chain is empty / has never been checkpointed.
    /// </summary>
    public static AuditCheckpointResult UpToDate(AuditCheckpoint? latest) =>
        new(AuditCheckpointOutcome.UpToDate, latest, AuditChainVerificationResult.Ok);

    public static AuditCheckpointResult ChainBroken(AuditChainVerificationResult verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (verification.Intact)
            throw new ArgumentException("A ChainBroken result requires a broken verification.", nameof(verification));
        return new(AuditCheckpointOutcome.ChainBroken, null, verification);
    }
}
