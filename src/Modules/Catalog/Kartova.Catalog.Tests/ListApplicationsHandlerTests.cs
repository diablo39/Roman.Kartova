using Kartova.Catalog.Contracts;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Pagination;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Unit-tier coverage for the dispatch-to-extension wiring around
/// <see cref="ListApplicationsHandler"/> — specifically the
/// <see cref="ApplicationSortSpecs.Resolve"/> branches and the
/// allowlist surfaced on rejection. End-to-end pagination behavior
/// (cursor encode/decode, RLS, real SQL) lives in
/// <c>Kartova.Catalog.IntegrationTests.ListApplicationsPaginationTests</c>.
/// </summary>
[TestClass]
public class ListApplicationsHandlerTests
{
    [TestMethod]
    public void Resolve_CreatedAt_returns_CreatedAt_sort_spec()
    {
        var spec = ApplicationSortSpecs.Resolve(ApplicationSortField.CreatedAt);

        Assert.AreSame(ApplicationSortSpecs.CreatedAt, spec);
        Assert.AreEqual("createdAt", spec.FieldName);
    }

    [TestMethod]
    public void Resolve_Name_returns_Name_sort_spec()
    {
        var spec = ApplicationSortSpecs.Resolve(ApplicationSortField.Name);

        Assert.AreSame(ApplicationSortSpecs.Name, spec);
        Assert.AreEqual("name", spec.FieldName);
    }

    [TestMethod]
    public void Resolve_undefined_enum_value_throws_InvalidSortFieldException_with_allowlist()
    {
        // (ApplicationSortField)999 is the exact path Enum.TryParse takes when given a
        // numeric query string — Enum.IsDefined would normally reject it at the
        // endpoint boundary, but the spec mandates the inner Resolve() also be hardened
        // so a future code path (e.g. internal caller bypassing the endpoint guard)
        // cannot fall through to a default sort silently.
        var ex = Assert.ThrowsExactly<InvalidSortFieldException>(
            () => ApplicationSortSpecs.Resolve((ApplicationSortField)999));
        CollectionAssert.AreEquivalent(new[] { "createdAt", "name" }, ex.AllowedFields.ToArray());
    }

    [TestMethod]
    public void AllowedFieldNames_lists_only_supported_fields()
    {
        CollectionAssert.AreEquivalent(
            new[] { "createdAt", "name" },
            ApplicationSortSpecs.AllowedFieldNames.ToArray());
    }
}
