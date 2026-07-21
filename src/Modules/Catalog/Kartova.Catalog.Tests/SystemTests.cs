using Kartova.SharedKernel.Multitenancy;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public class SystemTests
{
    private static TenantId T() => new(Guid.NewGuid());

    private static CatalogSystem Create(string name = "Payments Platform", string? desc = "desc") =>
        CatalogSystem.Create(name, desc, Guid.NewGuid(), Guid.NewGuid(), T(), TimeProvider.System);

    [TestMethod]
    public void Create_sets_fields_and_generates_id()
    {
        var s = Create();
        Assert.AreNotEqual(Guid.Empty, s.Id.Value);
        Assert.AreEqual("Payments Platform", s.DisplayName);
        Assert.AreEqual("desc", s.Description);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_rejects_empty_display_name(string name) =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(name));

    [TestMethod]
    public void Create_rejects_display_name_over_128() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(new string('x', 129)));

    [TestMethod]
    public void Create_rejects_description_over_4096() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(desc: new string('x', 4097)));

    [TestMethod]
    public void Create_rejects_empty_team() =>
        Assert.ThrowsExactly<ArgumentException>(() =>
            CatalogSystem.Create("ok", null, Guid.NewGuid(), Guid.Empty, T(), TimeProvider.System));

    [TestMethod]
    public void Create_rejects_empty_created_by() =>
        Assert.ThrowsExactly<ArgumentException>(() =>
            CatalogSystem.Create("ok", null, Guid.Empty, Guid.NewGuid(), T(), TimeProvider.System));

    [TestMethod]
    public void Create_allows_null_description() => Assert.IsNull(Create(desc: null).Description);

    // Boundary tests: kill the `> 128`→`>=` and `> 4096`→`>=` equality mutants —
    // the exact max length must be VALID (only strictly-over should throw).
    [TestMethod]
    public void Create_allows_display_name_exactly_128() =>
        Assert.AreEqual(128, Create(name: new string('x', 128)).DisplayName.Length);

    [TestMethod]
    public void Create_allows_description_exactly_4096() =>
        Assert.AreEqual(4096, Create(desc: new string('x', 4096)).Description!.Length);
}
