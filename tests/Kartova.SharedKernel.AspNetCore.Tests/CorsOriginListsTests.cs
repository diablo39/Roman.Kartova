using Kartova.SharedKernel.AspNetCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public class CorsOriginListsTests
{
    [TestMethod]
    public void Normalize_trims_whitespace_and_trailing_slash_and_drops_empty_entries()
    {
        var result = CorsOriginLists.Normalize(new[] { " https://admin.example/ ", "http://localhost:5174", "", "   " });

        CollectionAssert.AreEqual(new[] { "https://admin.example", "http://localhost:5174" }, result);
    }

    [TestMethod]
    public void Validate_accepts_disjoint_lists()
    {
        CorsOriginLists.Validate(new[] { "http://localhost:5173" }, new[] { "http://localhost:5174" });
    }

    [TestMethod]
    public void Validate_accepts_empty_lists()
    {
        CorsOriginLists.Validate(Array.Empty<string>(), Array.Empty<string>());
    }

    [TestMethod]
    public void Validate_rejects_an_origin_present_in_both_lists()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CorsOriginLists.Validate(new[] { "http://localhost:5173" }, new[] { "http://localhost:5173" }));

        StringAssert.Contains(ex.Message, "http://localhost:5173");
        StringAssert.Contains(ex.Message, CorsConfigKeys.AdminAllowedOrigins);
    }

    [TestMethod]
    public void Validate_rejects_overlap_that_differs_only_by_case_or_trailing_slash()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CorsOriginLists.Validate(new[] { "https://App.Example" }, new[] { "https://app.example/" }));
    }

    [TestMethod]
    public void Validate_rejects_wildcard_in_admin_list()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CorsOriginLists.Validate(Array.Empty<string>(), new[] { "*" }));

        StringAssert.Contains(ex.Message, "*");
    }

    [TestMethod]
    public void Validate_rejects_wildcard_tenant_list_when_admin_list_is_non_empty()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CorsOriginLists.Validate(new[] { "*" }, new[] { "http://localhost:5174" }));

        StringAssert.Contains(ex.Message, CorsConfigKeys.AllowedOrigins);
        StringAssert.Contains(ex.Message, "ADR-0118");
    }

    [TestMethod]
    public void Validate_accepts_wildcard_tenant_list_when_admin_list_is_empty()
    {
        CorsOriginLists.Validate(new[] { "*" }, Array.Empty<string>());
    }
}
