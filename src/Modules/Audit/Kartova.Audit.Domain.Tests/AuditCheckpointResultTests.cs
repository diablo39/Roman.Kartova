using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain.Tests;

[TestClass]
public class AuditCheckpointResultTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AuditCheckpoint Checkpoint() =>
        AuditCheckpoint.Create(Guid.NewGuid(), Tenant, 1, new byte[32], new DateTimeOffset(2026, 6, 16, 7, 0, 0, TimeSpan.Zero));

    [TestMethod]
    public void Created_carries_the_checkpoint_and_an_intact_verification()
    {
        var cp = Checkpoint();
        var result = AuditCheckpointResult.Created(cp);

        Assert.AreEqual(AuditCheckpointOutcome.Created, result.Outcome);
        Assert.AreSame(cp, result.Checkpoint);
        Assert.IsTrue(result.Verification.Intact);
    }

    [TestMethod]
    public void Created_rejects_null_checkpoint()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AuditCheckpointResult.Created(null!));
    }

    [TestMethod]
    public void UpToDate_passes_through_the_latest_checkpoint()
    {
        var cp = Checkpoint();
        var result = AuditCheckpointResult.UpToDate(cp);

        Assert.AreEqual(AuditCheckpointOutcome.UpToDate, result.Outcome);
        Assert.AreSame(cp, result.Checkpoint);
        Assert.IsTrue(result.Verification.Intact);
    }

    [TestMethod]
    public void UpToDate_allows_no_existing_checkpoint()
    {
        var result = AuditCheckpointResult.UpToDate(null);
        Assert.AreEqual(AuditCheckpointOutcome.UpToDate, result.Outcome);
        Assert.IsNull(result.Checkpoint);
    }

    [TestMethod]
    public void ChainBroken_carries_the_failing_verification_and_no_checkpoint()
    {
        var broken = AuditChainVerificationResult.Broken(7, "boom");
        var result = AuditCheckpointResult.ChainBroken(broken);

        Assert.AreEqual(AuditCheckpointOutcome.ChainBroken, result.Outcome);
        Assert.IsNull(result.Checkpoint);
        Assert.AreSame(broken, result.Verification);
    }

    [TestMethod]
    public void ChainBroken_rejects_an_intact_verification()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditCheckpointResult.ChainBroken(AuditChainVerificationResult.Ok));
    }

    [TestMethod]
    public void ChainBroken_rejects_null_verification()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AuditCheckpointResult.ChainBroken(null!));
    }
}
