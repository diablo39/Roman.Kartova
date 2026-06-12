using System.Collections.Generic;
using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain.Tests;

[TestClass]
public class AuditLogEntryTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public void Create_computes_row_hash_matching_the_hasher()
    {
        var id = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var when = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);
        var data = new Dictionary<string, string?> { ["new_role"] = "OrgAdmin" };

        var entry = AuditLogEntry.Create(
            id, Tenant, seq: 1, when, AuditActorType.User, actor, actorDisplay: "Ada",
            action: "member.role_changed", targetType: "User", targetId: actor.ToString(),
            data, AuditRowHasher.GenesisHash);

        var expected = AuditRowHasher.ComputeRowHash(
            Tenant, 1, when, AuditActorType.User, actor,
            "member.role_changed", "User", actor.ToString(), data, AuditRowHasher.GenesisHash);

        CollectionAssert.AreEqual(expected, entry.RowHash);
        Assert.AreEqual(1, entry.Seq);
        CollectionAssert.AreEqual(AuditRowHasher.GenesisHash, entry.PrevHash);
    }

    [TestMethod]
    public void Create_rejects_non_positive_seq()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 0, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: "x",
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void Create_rejects_wrong_length_prev_hash()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: "x",
            data: null, prevHash: new byte[16]));
    }
}
