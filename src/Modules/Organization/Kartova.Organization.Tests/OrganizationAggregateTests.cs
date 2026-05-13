using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

[TestClass]
public class OrganizationAggregateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null)
    {
        var c = new FakeTimeProvider();
        c.SetUtcNow(now ?? Now);
        return c;
    }

    [TestMethod]
    public void Create_with_valid_name_sets_tenant_id_equal_to_id_and_uses_clock_for_CreatedAt()
    {
        var clock = Clock();

        var org = Domain.Organization.Create("Acme", clock);

        Assert.AreNotEqual(Guid.Empty, org.Id.Value);
        Assert.AreEqual(org.Id.Value, org.TenantId.Value);
        Assert.AreEqual("Acme", org.Name);
        Assert.AreEqual(clock.GetUtcNow(), org.CreatedAt);
    }

    [TestMethod]
    public void Create_with_null_clock_throws()
    {
        var ex = Assert.ThrowsExactly<ArgumentNullException>(
            () => Domain.Organization.Create("Acme", clock: null!));
        Assert.AreEqual("clock", ex.ParamName);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_with_empty_name_throws(string? name)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => Domain.Organization.Create(name!, Clock()));
    }

    [TestMethod]
    public void Create_with_too_long_name_throws()
    {
        var name = new string('a', 101);
        Assert.ThrowsExactly<ArgumentException>(
            () => Domain.Organization.Create(name, Clock()));
    }

    [TestMethod]
    public void Rename_updates_name()
    {
        var org = Domain.Organization.Create("Acme", Clock());
        org.Rename("NewName");
        Assert.AreEqual("NewName", org.Name);
    }
}
