using Kartova.SharedKernel.Pagination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests.Pagination;

[TestClass]
public sealed class CursorFilterComparerTests
{
    private static IReadOnlyDictionary<string, string> Map(params (string K, string V)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    [TestMethod]
    public void Equal_maps_return_null()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(("includeDecommissioned", "false"), ("ownerUserId", "abc")),
            Map(("ownerUserId", "abc"), ("includeDecommissioned", "false"))); // order-independent

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Empty_vs_empty_returns_null()
    {
        Assert.IsNull(CursorFilterComparer.FindMismatch(Map(), Map()));
    }

    [TestMethod]
    public void Key_only_in_cursor_reports_none_as_actual()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(("ownerUserId", "G1")), Map());

        Assert.IsNotNull(result);
        Assert.AreEqual("ownerUserId", result.Value.Name);
        Assert.AreEqual("G1", result.Value.Expected);
        Assert.AreEqual("(none)", result.Value.Actual);
    }

    [TestMethod]
    public void Key_only_in_request_reports_none_as_expected()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(), Map(("ownerUserId", "G2")));

        Assert.IsNotNull(result);
        Assert.AreEqual("ownerUserId", result.Value.Name);
        Assert.AreEqual("(none)", result.Value.Expected);
        Assert.AreEqual("G2", result.Value.Actual);
    }

    [TestMethod]
    public void Differing_value_reports_both_sides()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(("includeDecommissioned", "true")),
            Map(("includeDecommissioned", "false")));

        Assert.IsNotNull(result);
        Assert.AreEqual("includeDecommissioned", result.Value.Name);
        Assert.AreEqual("true", result.Value.Expected);
        Assert.AreEqual("false", result.Value.Actual);
    }

    [TestMethod]
    public void Multiple_differences_return_first_by_ordinal_key()
    {
        // Keys "a" and "z" both differ; ordinal-sorted first is "a".
        var result = CursorFilterComparer.FindMismatch(
            Map(("z", "1"), ("a", "1")),
            Map(("z", "2"), ("a", "2")));

        Assert.IsNotNull(result);
        Assert.AreEqual("a", result.Value.Name);
    }

    [TestMethod]
    public void Value_comparison_is_ordinal_case_sensitive()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(("k", "Value")), Map(("k", "value")));

        Assert.IsNotNull(result);
        Assert.AreEqual("k", result.Value.Name);
    }

    [TestMethod]
    public void Key_comparison_is_ordinal_case_sensitive()
    {
        // "K" (cursor) and "k" (request) are distinct keys -> "K" is only-in-cursor.
        // Ordinal sort places uppercase 'K' (0x4B) before lowercase 'k' (0x6B),
        // so the first reported difference is "K".
        var result = CursorFilterComparer.FindMismatch(
            Map(("K", "v")), Map(("k", "v")));

        Assert.IsNotNull(result);
        Assert.AreEqual("K", result.Value.Name);
        Assert.AreEqual("v", result.Value.Expected);
        Assert.AreEqual("(none)", result.Value.Actual);
    }
}
