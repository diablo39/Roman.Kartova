using System.Collections.Generic;
using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain.Tests;

[TestClass]
public class AuditChainInspectorTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static AuditLogEntry Row(long seq, byte[] prev, string action = "a") =>
        AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq, new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero),
            AuditActorType.User, Actor, actorDisplay: null,
            action, targetType: "User", targetId: "x",
            data: new Dictionary<string, string?> { ["k"] = "v" }, prev);

    private static List<AuditLogEntry> Chain(int n)
    {
        var rows = new List<AuditLogEntry>();
        var prev = AuditRowHasher.GenesisHash;
        for (var i = 1; i <= n; i++) { var r = Row(i, prev); rows.Add(r); prev = r.RowHash; }
        return rows;
    }

    // Survivor 4: AuditChainInspector.cs:13 — Statement mutation: `ArgumentNullException.ThrowIfNull` removed
    [TestMethod]
    public void Inspect_throws_for_null_input()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AuditChainInspector.Inspect(null!));
    }

    [TestMethod]
    public void Empty_chain_is_intact()
    {
        var result = AuditChainInspector.Inspect(new List<AuditLogEntry>());
        Assert.IsTrue(result.Intact);
    }

    [TestMethod]
    public void Well_formed_chain_is_intact()
    {
        Assert.IsTrue(AuditChainInspector.Inspect(Chain(3)).Intact);
    }

    [TestMethod]
    public void Detects_non_contiguous_seq()
    {
        var rows = Chain(3);
        rows.RemoveAt(1); // drop seq 2 -> 1,3
        var result = AuditChainInspector.Inspect(rows);
        Assert.IsFalse(result.Intact);
        Assert.AreEqual(3, result.FirstBrokenSeq);
    }

    [TestMethod]
    public void Detects_prev_hash_break()
    {
        var rows = Chain(2);
        // Replace row 2 with one that chains off genesis instead of row1.RowHash → prev_hash mismatch.
        rows[1] = Row(2, new byte[32]);
        var result = AuditChainInspector.Inspect(rows);
        Assert.IsFalse(result.Intact);
        Assert.AreEqual(2, result.FirstBrokenSeq);
    }

    [TestMethod]
    public void Detects_tampered_row_hash()
    {
        var rows = Chain(2);
        // Unit-only forgery; requires a settable RowHash backing field.
        typeof(AuditLogEntry).GetProperty(nameof(AuditLogEntry.RowHash))!
            .SetValue(rows[0], new byte[32]); // forge row 1's stored hash
        var result = AuditChainInspector.Inspect(rows);
        Assert.IsFalse(result.Intact);
        Assert.AreEqual(1, result.FirstBrokenSeq);
    }

    [TestMethod]
    public void Detects_tampered_row_hash_on_last_row()
    {
        var rows = Chain(3);
        // Unit-only forgery (simulates post-write tampering the DB grants prevent in prod):
        // requires AuditLogEntry.RowHash to have a settable backing field. If this throws
        // "Property set method not found", replace with an internal test-factory overload.
        typeof(AuditLogEntry).GetProperty(nameof(AuditLogEntry.RowHash))!
            .SetValue(rows[2], new byte[32]); // forge seq 3 (last row)
        var result = AuditChainInspector.Inspect(rows);
        Assert.IsFalse(result.Intact);
        Assert.AreEqual(3, result.FirstBrokenSeq);
    }
}
