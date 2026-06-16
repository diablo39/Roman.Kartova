using System.Collections.Generic;
using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain.Tests;

[TestClass]
public class AuditChainWalkerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static AuditLogEntry Row(long seq, byte[] prev) =>
        AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq, new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero),
            AuditActorType.User, Actor, actorDisplay: null,
            action: "a", targetType: "User", targetId: "x",
            data: new Dictionary<string, string?> { ["k"] = "v" }, prev);

    private static List<AuditLogEntry> Chain(int n)
    {
        var rows = new List<AuditLogEntry>();
        var prev = AuditRowHasher.GenesisHash;
        for (var i = 1; i <= n; i++) { var r = Row(i, prev); rows.Add(r); prev = r.RowHash; }
        return rows;
    }

    [TestMethod]
    public void Default_walker_starts_at_genesis_and_accepts_a_well_formed_chain()
    {
        var walker = new AuditChainWalker();
        foreach (var row in Chain(3))
            Assert.IsTrue(walker.Step(row));
        Assert.IsTrue(walker.Result.Intact);
    }

    [TestMethod]
    public void Seeded_walker_resumes_the_tail_after_a_checkpoint()
    {
        var rows = Chain(5);
        var checkpoint = rows[2]; // seq 3 is the checkpoint head

        var walker = new AuditChainWalker(checkpoint.Seq + 1, checkpoint.RowHash);
        Assert.IsTrue(walker.Step(rows[3])); // seq 4
        Assert.IsTrue(walker.Step(rows[4])); // seq 5
        Assert.IsTrue(walker.Result.Intact);
    }

    [TestMethod]
    public void Seeded_walker_rejects_a_tail_whose_first_row_does_not_chain_off_the_checkpoint()
    {
        var rows = Chain(5);
        // Seed at seq 4 but with the wrong prev hash (genesis) → first tail row's prev_hash mismatches.
        var walker = new AuditChainWalker(4, AuditRowHasher.GenesisHash);
        Assert.IsFalse(walker.Step(rows[3]));
        Assert.IsFalse(walker.Result.Intact);
        Assert.AreEqual(4, walker.Result.FirstBrokenSeq);
        Assert.AreEqual("prev_hash does not match prior row_hash", walker.Result.Reason);
    }

    [TestMethod]
    public void Seeded_walker_detects_a_seq_gap_in_the_tail()
    {
        var rows = Chain(5);
        var checkpoint = rows[2]; // seq 3
        var walker = new AuditChainWalker(checkpoint.Seq + 1, checkpoint.RowHash);
        // Feed seq 5 when seq 4 was expected.
        Assert.IsFalse(walker.Step(rows[4]));
        Assert.AreEqual(5, walker.Result.FirstBrokenSeq);
        Assert.AreEqual("non-contiguous seq (expected 4)", walker.Result.Reason);
    }

    [TestMethod]
    public void Step_after_a_break_returns_false_and_keeps_the_first_break()
    {
        var rows = Chain(3);
        var walker = new AuditChainWalker(2, AuditRowHasher.GenesisHash); // wrong seed → break on first step
        Assert.IsFalse(walker.Step(rows[0])); // seq 1 fed when 2 expected
        var firstReason = walker.Result.Reason;
        Assert.IsFalse(walker.Step(rows[1])); // ignored
        Assert.AreEqual(firstReason, walker.Result.Reason);
    }

    [TestMethod]
    public void Step_throws_for_null_row()
    {
        var walker = new AuditChainWalker();
        Assert.ThrowsExactly<ArgumentNullException>(() => walker.Step(null!));
    }

    [TestMethod]
    public void Seed_ctor_rejects_null_prev_hash()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new AuditChainWalker(1, null!));
    }

    [TestMethod]
    public void Seed_ctor_rejects_seq_below_one()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AuditChainWalker(0, AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void Seed_ctor_rejects_wrong_length_prev_hash()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new AuditChainWalker(2, new byte[16]));
    }

    [TestMethod]
    public void Seeded_walker_fed_no_rows_is_intact()
    {
        // The in-memory analog of an up-to-date checkpoint with an empty tail.
        var rows = Chain(3);
        var checkpoint = rows[2];
        var walker = new AuditChainWalker(checkpoint.Seq + 1, checkpoint.RowHash);
        Assert.IsTrue(walker.Result.Intact);
    }

    [TestMethod]
    public void Seed_ctor_copies_the_prev_hash_buffer()
    {
        var seed = (byte[])AuditRowHasher.GenesisHash;
        var rows = Chain(1); // row 1 chains off genesis
        var walker = new AuditChainWalker(1, seed);
        seed[0] ^= 0xFF; // mutate caller's buffer after construction
        // The walk must still accept row 1 (whose prev_hash is genesis), proving the seed was copied.
        Assert.IsTrue(walker.Step(rows[0]));
        Assert.IsTrue(walker.Result.Intact);
    }
}
