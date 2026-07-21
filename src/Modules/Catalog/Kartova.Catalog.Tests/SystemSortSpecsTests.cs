using Kartova.Catalog.Contracts;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Unit tests for <see cref="SystemSortSpecs"/> — the Systems list sort allowlist
/// (ADR-0095 §5, design §5). Mirrors the intent of <c>ApiSortSpecs</c>.
/// </summary>
[TestClass]
public class SystemSortSpecsTests
{
    [TestMethod]
    public void AllowedFieldNames_contains_exactly_displayName_and_createdAt()
    {
        CollectionAssert.AreEqual(
            new[] { "displayName", "createdAt" },
            SystemSortSpecs.AllowedFieldNames.ToArray());
    }

    [TestMethod]
    public void Resolve_DisplayName_returns_the_displayName_spec()
    {
        var spec = SystemSortSpecs.Resolve(SystemSortField.DisplayName);
        Assert.AreEqual("displayName", spec.FieldName);
    }

    [TestMethod]
    public void Resolve_CreatedAt_returns_the_createdAt_spec()
    {
        var spec = SystemSortSpecs.Resolve(SystemSortField.CreatedAt);
        Assert.AreEqual("createdAt", spec.FieldName);
    }

    [TestMethod]
    public void Resolve_unknown_field_throws_InvalidSortFieldException()
    {
        const SystemSortField unknown = (SystemSortField)99;

        var ex = Assert.ThrowsExactly<InvalidSortFieldException>(() => SystemSortSpecs.Resolve(unknown));
        CollectionAssert.AreEqual(
            new[] { "displayName", "createdAt" },
            ex.AllowedFields.ToArray());
    }
}
