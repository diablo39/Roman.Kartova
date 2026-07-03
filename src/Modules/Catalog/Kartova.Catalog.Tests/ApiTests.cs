using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ApiTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Team = Guid.NewGuid();
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-07-03T10:00:00Z"));

    private static Api Create(
        string name = "orders-api", string desc = "Orders REST API.", ApiStyle style = ApiStyle.Rest,
        string version = "v1", string? specUrl = "https://specs.example.com/orders/openapi.json",
        Guid? creator = null, Guid? team = null)
        => Api.Create(name, desc, style, version, specUrl, creator ?? Creator, team ?? Team, Tenant, Clock);

    [TestMethod]
    public void Create_with_valid_args_sets_fields()
    {
        var a = Create();
        Assert.AreEqual("orders-api", a.DisplayName);
        Assert.AreEqual("Orders REST API.", a.Description);
        Assert.AreEqual(ApiStyle.Rest, a.Style);
        Assert.AreEqual("v1", a.Version);
        Assert.AreEqual("https://specs.example.com/orders/openapi.json", a.SpecUrl);
        Assert.AreEqual(Creator, a.CreatedByUserId);
        Assert.AreEqual(Team, a.TeamId);
        Assert.AreEqual(Tenant, a.TenantId);
        Assert.AreEqual(Clock.GetUtcNow(), a.CreatedAt);
        Assert.AreNotEqual(Guid.Empty, a.Id.Value);
    }

    [TestMethod]
    public void Create_allows_null_spec_url()
    {
        var a = Create(specUrl: null);
        Assert.IsNull(a.SpecUrl);
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
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_version(string version) =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(version: version));

    [TestMethod]
    public void Create_throws_on_version_over_64() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(version: new string('9', 65)));

    [TestMethod]
    public void Create_accepts_version_of_exactly_64() =>
        Assert.AreEqual(64, Create(version: new string('9', 64)).Version.Length);

    [TestMethod]
    public void Create_throws_on_undefined_style() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(style: (ApiStyle)999));

    [TestMethod]
    public void Create_throws_on_relative_spec_url() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(specUrl: "/openapi.json"));

    [TestMethod]
    public void Create_throws_on_spec_url_over_2048() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(specUrl: "https://x.example.com/" + new string('a', 2048)));

    [TestMethod]
    public void Create_accepts_spec_url_of_exactly_2048()
    {
        const string prefix = "https://x.example.com/";
        var specUrl = prefix + new string('a', 2048 - prefix.Length);
        Assert.AreEqual(2048, specUrl.Length);
        Assert.AreEqual(2048, Create(specUrl: specUrl).SpecUrl!.Length);
    }

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
            () => Api.Create("a", "d", ApiStyle.Rest, "v1", null, Creator, Team, Tenant, nullClock!));
    }
}
