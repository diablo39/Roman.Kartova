using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public class CatalogEnvironmentTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static CatalogEnvironment Create(
        string displayName = "Production EU",
        string description = "Primary prod cluster",
        EnvironmentType type = EnvironmentType.Production,
        string? region = "eu-west-1",
        string resourceDetailsJson = "{}")
        => CatalogEnvironment.Create(displayName, description, type, region,
            resourceDetailsJson, User, Tenant, new FakeTimeProvider());

    [TestMethod]
    public void Create_sets_all_fields()
    {
        var env = Create();
        Assert.AreEqual("Production EU", env.DisplayName);
        Assert.AreEqual("Primary prod cluster", env.Description);
        Assert.AreEqual(EnvironmentType.Production, env.Type);
        Assert.AreEqual("eu-west-1", env.Region);
        Assert.AreEqual("{}", env.ResourceDetails);
        Assert.AreEqual(User, env.CreatedByUserId);
        Assert.AreEqual(Tenant, env.TenantId);
    }

    [DataRow("")]
    [DataRow("   ")]
    [TestMethod]
    public void Create_rejects_blank_display_name(string name)
        => Assert.ThrowsExactly<ArgumentException>(() => Create(displayName: name));

    [TestMethod]
    public void Create_rejects_display_name_over_128()
        => Assert.ThrowsExactly<ArgumentException>(() => Create(displayName: new string('x', 129)));

    [DataRow("")]
    [DataRow("   ")]
    [TestMethod]
    public void Create_rejects_blank_description(string desc)
        => Assert.ThrowsExactly<ArgumentException>(() => Create(description: desc));

    [TestMethod]
    public void Create_rejects_description_over_4096()
        => Assert.ThrowsExactly<ArgumentException>(() => Create(description: new string('x', 4097)));

    [TestMethod]
    public void Create_rejects_unknown_type()
        => Assert.ThrowsExactly<ArgumentException>(() => Create(type: (EnvironmentType)99));

    [TestMethod]
    public void Create_rejects_region_over_256()
        => Assert.ThrowsExactly<ArgumentException>(() => Create(region: new string('x', 257)));

    [TestMethod]
    public void Create_normalizes_blank_region_to_null()
    {
        var env = Create(region: "  ");
        Assert.IsNull(env.Region);
    }

    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not-json")]
    [TestMethod]
    public void Create_rejects_invalid_resource_details_json(string json)
        => Assert.ThrowsExactly<ArgumentException>(() => Create(resourceDetailsJson: json));

    [TestMethod]
    public void Create_rejects_empty_created_by()
        => Assert.ThrowsExactly<ArgumentException>(() =>
            CatalogEnvironment.Create("n", "d", EnvironmentType.Development, null, "{}",
                Guid.Empty, Tenant, new FakeTimeProvider()));

    [TestMethod]
    public void Edit_replaces_mutable_fields()
    {
        var env = Create();
        env.Edit("Staging APAC", "Secondary staging cluster", "ap-southeast-1", "{\"k\":\"v\"}");

        Assert.AreEqual("Staging APAC", env.DisplayName);
        Assert.AreEqual("Secondary staging cluster", env.Description);
        Assert.AreEqual("ap-southeast-1", env.Region);
        Assert.AreEqual("{\"k\":\"v\"}", env.ResourceDetails);
    }

    [TestMethod]
    public void Edit_leaves_type_unchanged()
    {
        var env = Create(type: EnvironmentType.Production);
        env.Edit("Renamed", env.Description, env.Region, env.ResourceDetails);
        Assert.AreEqual(EnvironmentType.Production, env.Type);
    }

    [TestMethod]
    public void Edit_normalizes_blank_region_to_null()
    {
        var env = Create();
        env.Edit(env.DisplayName, env.Description, "   ", env.ResourceDetails);
        Assert.IsNull(env.Region);
    }

    [DataRow("")]
    [DataRow("   ")]
    [TestMethod]
    public void Edit_rejects_blank_display_name(string name)
    {
        var env = Create();
        Assert.ThrowsExactly<ArgumentException>(() => env.Edit(name, env.Description, env.Region, env.ResourceDetails));
    }

    [TestMethod]
    public void Edit_rejects_display_name_over_128()
    {
        var env = Create();
        Assert.ThrowsExactly<ArgumentException>(() =>
            env.Edit(new string('x', 129), env.Description, env.Region, env.ResourceDetails));
    }

    [DataRow("")]
    [DataRow("   ")]
    [TestMethod]
    public void Edit_rejects_blank_description(string desc)
    {
        var env = Create();
        Assert.ThrowsExactly<ArgumentException>(() => env.Edit(env.DisplayName, desc, env.Region, env.ResourceDetails));
    }

    [TestMethod]
    public void Edit_rejects_description_over_4096()
    {
        var env = Create();
        Assert.ThrowsExactly<ArgumentException>(() =>
            env.Edit(env.DisplayName, new string('x', 4097), env.Region, env.ResourceDetails));
    }

    [TestMethod]
    public void Edit_rejects_region_over_256()
    {
        var env = Create();
        Assert.ThrowsExactly<ArgumentException>(() =>
            env.Edit(env.DisplayName, env.Description, new string('x', 257), env.ResourceDetails));
    }

    [DataRow("")]
    [DataRow("not-json")]
    [TestMethod]
    public void Edit_rejects_invalid_resource_details_json(string json)
    {
        var env = Create();
        Assert.ThrowsExactly<ArgumentException>(() => env.Edit(env.DisplayName, env.Description, env.Region, json));
    }
}
