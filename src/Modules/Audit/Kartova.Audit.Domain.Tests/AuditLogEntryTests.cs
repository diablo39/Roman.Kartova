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
    [DataRow(0)]
    [DataRow(16)]
    [DataRow(33)]
    public void Create_rejects_wrong_length_prev_hash(int length)
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: "x",
            data: null, prevHash: new byte[length]));
    }

    [TestMethod]
    public void Create_rejects_empty_id()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditLogEntry.Create(
            Guid.Empty, Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: "x",
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void Create_rejects_empty_tenant_id()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Guid.Empty, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: "x",
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void Create_rejects_null_action()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException (a subclass) for null
        Assert.ThrowsExactly<ArgumentNullException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: null!, targetType: "User", targetId: "x",
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_rejects_blank_action(string action)
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: action, targetType: "User", targetId: "x",
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void Create_rejects_user_actor_with_empty_actor_id()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.Empty,
            actorDisplay: null, action: "a", targetType: "User", targetId: "x",
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    // Survivors 5-7: AuditLogEntry.cs:43,45,46 — guard statements removed by mutant
    // Line 43: ArgumentNullException.ThrowIfNull(prevHash)
    [TestMethod]
    public void Create_rejects_null_prev_hash()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: "x",
            data: null, prevHash: null!));
    }

    // Line 45: ArgumentException.ThrowIfNullOrWhiteSpace(targetType) — null throws ArgumentNullException
    [TestMethod]
    public void Create_rejects_null_target_type()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: null!, targetId: "x",
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_rejects_blank_target_type(string targetType)
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: targetType, targetId: "x",
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    // Line 46: ArgumentException.ThrowIfNullOrWhiteSpace(targetId) — null throws ArgumentNullException
    [TestMethod]
    public void Create_rejects_null_target_id()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: null!,
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_rejects_blank_target_id(string targetId)
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: targetId,
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void Create_round_trips_all_stored_fields()
    {
        var id = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var when = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);
        var data = new Dictionary<string, string?> { ["k"] = "v" };

        var entry = AuditLogEntry.Create(
            id, Tenant, seq: 7, when, AuditActorType.System, actor, actorDisplay: "Ada",
            action: "x.done", targetType: "Team", targetId: "t1", data, AuditRowHasher.GenesisHash);

        Assert.AreEqual(id, entry.Id);
        Assert.AreEqual(Tenant, entry.TenantId);
        Assert.AreEqual(7, entry.Seq);
        Assert.AreEqual(when, entry.OccurredAt);
        Assert.AreEqual(AuditActorType.System, entry.ActorType);
        Assert.AreEqual(actor, entry.ActorId);
        Assert.AreEqual("Ada", entry.ActorDisplay);
        Assert.AreEqual("x.done", entry.Action);
        Assert.AreEqual("Team", entry.TargetType);
        Assert.AreEqual("t1", entry.TargetId);
        Assert.AreSame(data, entry.Data);
    }
}
