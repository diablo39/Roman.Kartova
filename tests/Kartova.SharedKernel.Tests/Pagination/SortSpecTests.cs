using Kartova.SharedKernel.Pagination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests.Pagination;

[TestClass]
public sealed class SortSpecTests
{
    private sealed record SampleEntity(string Name, DateTimeOffset CreatedAt, Guid Id);

    [TestMethod]
    public void Construction_captures_field_name_and_key_selector()
    {
        var spec = new SortSpec<SampleEntity>("name", x => x.Name);

        Assert.AreEqual("name", spec.FieldName);
        Assert.AreEqual(
            "x",
            spec.KeySelector.Compile().Invoke(new SampleEntity("x", DateTimeOffset.UtcNow, Guid.NewGuid())));
    }

    [TestMethod]
    public void Two_specs_with_different_lambda_instances_are_not_value_equal()
    {
        var a = new SortSpec<SampleEntity>("name", x => x.Name);
        var b = new SortSpec<SampleEntity>("name", x => x.Name);

        // SortSpec is a record, but its KeySelector is an Expression<> with reference-only equality.
        // Two specs with the same field name but distinct lambda literals are NOT value-equal.
        // Callers MUST treat SortSpec by FieldName, not by record equality. ADR-0095 §5.
        Assert.AreNotEqual(b, a);
        Assert.AreEqual(b.FieldName, a.FieldName);
    }

    [TestMethod]
    public void CompiledKeySelector_caches_the_delegate_across_accesses()
    {
        // Kills mutant at line 24: `_compiled ??= KeySelector.Compile()` mutated to `_compiled = KeySelector.Compile()`.
        // With original ??= the first access compiles once and caches; subsequent accesses return the same instance.
        // With mutated =, every access recompiles, producing a new delegate instance → AreSame fails.
        var spec = new SortSpec<SampleEntity>("name", x => x.Name);

        var first = spec.CompiledKeySelector;
        var second = spec.CompiledKeySelector;
        var third = spec.CompiledKeySelector;

        Assert.AreSame(second, first);
        Assert.AreSame(third, second);
    }
}
