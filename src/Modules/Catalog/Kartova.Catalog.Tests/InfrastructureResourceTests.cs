using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public class InfrastructureResourceTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Team = Guid.NewGuid();
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-07-03T10:00:00Z"));

    private static InfrastructureResource Create(
        string name = "web-01", string desc = "prod web VM", string? provider = null, InfrastructureType type = InfrastructureType.VirtualMachine,
        string attributesJson = "{}", Guid? creator = null, Guid? team = null)
        => InfrastructureResource.Create(name, desc, provider, type, attributesJson, creator ?? Creator, team ?? Team, Tenant, Clock);

    [TestMethod]
    public void Create_with_valid_args_sets_all_fields()
    {
        var r = Create();
        Assert.AreEqual("web-01", r.DisplayName);
        Assert.AreEqual("prod web VM", r.Description);
        Assert.AreEqual(InfrastructureType.VirtualMachine, r.Type);
        Assert.AreEqual("{}", r.Attributes);
        Assert.AreEqual(Creator, r.CreatedByUserId);
        Assert.AreEqual(Team, r.TeamId);
        Assert.AreEqual(Tenant, r.TenantId);
        Assert.AreEqual(Clock.GetUtcNow(), r.CreatedAt);
        Assert.IsNull(r.SystemId);
        Assert.AreNotEqual(Guid.Empty, r.Id.Value);
    }

    [TestMethod]
    public void Create_generates_fresh_id_each_call() =>
        Assert.AreNotEqual(Create().Id.Value, Create().Id.Value);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_display_name(string name) =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(name: name));

    [TestMethod]
    public void Create_throws_on_display_name_over_128() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(name: new string('x', 129)));

    [TestMethod]
    public void Create_accepts_display_name_of_exactly_128() =>
        Assert.AreEqual(128, Create(name: new string('x', 128)).DisplayName.Length);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_description(string desc) =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(desc: desc));

    [TestMethod]
    public void Create_throws_on_description_over_4096() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(desc: new string('x', 4097)));

    [TestMethod]
    public void Create_accepts_description_of_exactly_4096() =>
        Assert.AreEqual(4096, Create(desc: new string('x', 4096)).Description.Length);

    [TestMethod]
    public void Create_rejects_unknown_type() =>
        Assert.ThrowsExactly<ArgumentException>(() => InfrastructureResource.Create(
            "n", "d", null, (InfrastructureType)999, "{}", Guid.NewGuid(), Guid.NewGuid(),
            new TenantId(Guid.NewGuid()), TimeProvider.System));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_blank_attributes(string attrs) =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(attributesJson: attrs));

    [TestMethod]
    public void Create_throws_on_empty_created_by() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(creator: Guid.Empty));

    [TestMethod]
    public void Create_throws_on_empty_team() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(team: Guid.Empty));

    [TestMethod]
    public void Create_with_null_TimeProvider_throws()
    {
        TimeProvider? nullClock = null;
        Assert.ThrowsExactly<ArgumentNullException>(
            () => InfrastructureResource.Create("a", "d", null, InfrastructureType.VirtualMachine, "{}", Creator, Team, Tenant, nullClock!));
    }

    [TestMethod]
    public void Create_with_explicit_createdAt_sets_CreatedAt()
    {
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var r = InfrastructureResource.Create("a", "d", null, InfrastructureType.VirtualMachine, "{}",
            Creator, Team, Tenant, createdAt);
        Assert.AreEqual(createdAt, r.CreatedAt);
    }

    [TestMethod]
    public void TeamId_is_exposed_via_ITeamScopedResource()
    {
        ITeamScopedResource r = Create();
        Assert.AreEqual(Team, r.TeamId);
    }

    [TestMethod]
    public void TenantId_is_exposed_via_ITenantOwned()
    {
        ITenantOwned r = Create();
        Assert.AreEqual(Tenant, r.TenantId);
    }

    [TestMethod]
    public void Create_StoresProvider()
    {
        var vm = InfrastructureResource.Create(
            "vm-01", "seeded", "AWS", InfrastructureType.VirtualMachine, "{}",
            Guid.NewGuid(), Guid.NewGuid(), Tenant, DateTimeOffset.UnixEpoch);

        Assert.AreEqual("AWS", vm.Provider);
    }

    [TestMethod]
    public void Create_AllowsNullProvider()
    {
        var vm = InfrastructureResource.Create(
            "vm-02", "seeded", null, InfrastructureType.VirtualMachine, "{}",
            Guid.NewGuid(), Guid.NewGuid(), Tenant, DateTimeOffset.UnixEpoch);

        Assert.IsNull(vm.Provider);
    }
}
