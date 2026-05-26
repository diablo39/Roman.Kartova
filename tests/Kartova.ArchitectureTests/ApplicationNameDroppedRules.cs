using System.Reflection;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Pins the slice-8 / ADR-0098 invariant that <c>Application.Name</c> (the
/// kebab-case slug introduced in slice 3) has been retroactively dropped
/// from the domain, contracts, and sort allowlist. UUIDs are now the
/// canonical entity identifier — no human-readable slug surface anywhere.
/// </summary>
[TestClass]
public sealed class ApplicationNameDroppedRules
{
    [TestMethod]
    public void Application_does_not_expose_a_Name_property()
    {
        var prop = typeof(Kartova.Catalog.Domain.Application).GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(prop, "Application.Name was dropped per ADR-0098; do not reintroduce.");
    }

    [TestMethod]
    public void ApplicationResponse_does_not_expose_a_Name_property()
    {
        var prop = typeof(Kartova.Catalog.Contracts.ApplicationResponse).GetProperty("Name");
        Assert.IsNull(prop, "ApplicationResponse.Name was dropped per ADR-0098; do not reintroduce.");
    }

    [TestMethod]
    public void ApplicationSortField_does_not_define_a_Name_value()
    {
        Assert.IsFalse(
            Enum.GetNames(typeof(Kartova.Catalog.Contracts.ApplicationSortField)).Contains("Name"),
            "ApplicationSortField.Name was dropped per ADR-0098; do not reintroduce.");
    }
}
