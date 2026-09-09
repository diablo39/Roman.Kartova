using Kartova.Catalog.Infrastructure;

namespace Kartova.Catalog.Tests;

/// <summary>
/// gate-7 T5: direct unit tests for <see cref="JsonbFunctions.JsonbExtractPathText"/>'s
/// client-side/in-memory path — the same body that <c>SortSpec{TEntity}.CompiledKeySelector</c>
/// invokes to encode a cursor boundary value (see the type's doc comment). Covers the documented
/// fallback behavior directly, rather than only indirectly via <see cref="VmSortSpecsTests"/> and
/// the real-Postgres EXPLAIN coverage in <c>InfrastructureVmSortTests</c> (which exercise the
/// EF-translated SQL path, not this in-memory one).
/// </summary>
[TestClass]
public class JsonbFunctionsTests
{
    [TestMethod]
    public void MissingKey_ReturnsNull() =>
        Assert.IsNull(JsonbFunctions.JsonbExtractPathText("""{"os":"linux"}""", "hostname"));

    [TestMethod]
    public void ExplicitJsonNull_ReturnsNull() =>
        Assert.IsNull(JsonbFunctions.JsonbExtractPathText("""{"hostname":null}""", "hostname"));

    [TestMethod]
    public void StringValue_ReturnsUnquoted() =>
        Assert.AreEqual("web-01", JsonbFunctions.JsonbExtractPathText("""{"hostname":"web-01"}""", "hostname"));

    [TestMethod]
    public void NumberValue_ReturnsRawJsonText() =>
        Assert.AreEqual("4", JsonbFunctions.JsonbExtractPathText("""{"vcpu":4}""", "vcpu"));

    [TestMethod]
    public void BoolValue_ReturnsRawJsonText() =>
        Assert.AreEqual("true", JsonbFunctions.JsonbExtractPathText("""{"flag":true}""", "flag"));
}
