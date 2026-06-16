using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain.Tests;

[TestClass]
public class AuditCheckpointTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static byte[] Hash32() => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly DateTimeOffset When = new(2026, 6, 16, 7, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Create_populates_all_fields()
    {
        var id = Guid.NewGuid();
        var hash = Hash32();
        var cp = AuditCheckpoint.Create(id, Tenant, seq: 42, hash, When);

        Assert.AreEqual(id, cp.Id);
        Assert.AreEqual(Tenant, cp.TenantId);
        Assert.AreEqual(42, cp.Seq);
        Assert.AreEqual(When, cp.CreatedAt);
        CollectionAssert.AreEqual(hash, cp.RowHash);
    }

    [TestMethod]
    public void Create_defensively_copies_the_hash()
    {
        var hash = Hash32();
        var cp = AuditCheckpoint.Create(Guid.NewGuid(), Tenant, 1, hash, When);
        hash[0] ^= 0xFF; // mutate the caller's buffer after construction
        Assert.AreNotEqual(hash[0], cp.RowHash[0]);
    }

    [TestMethod]
    public void Create_rejects_empty_id()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditCheckpoint.Create(Guid.Empty, Tenant, 1, Hash32(), When));
    }

    [TestMethod]
    public void Create_rejects_empty_tenant()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditCheckpoint.Create(Guid.NewGuid(), Guid.Empty, 1, Hash32(), When));
    }

    [TestMethod]
    public void Create_rejects_null_hash()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AuditCheckpoint.Create(Guid.NewGuid(), Tenant, 1, null!, When));
    }

    [TestMethod]
    public void Create_rejects_seq_below_one()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AuditCheckpoint.Create(Guid.NewGuid(), Tenant, 0, Hash32(), When));
    }

    [TestMethod]
    public void Create_rejects_wrong_hash_length()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditCheckpoint.Create(Guid.NewGuid(), Tenant, 1, new byte[16], When));
    }
}
