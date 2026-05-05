using FluentAssertions;
using Kartova.SharedKernel.Pagination;
using Xunit;

namespace Kartova.SharedKernel.Tests.Pagination;

public sealed class SortSpecTests
{
    private sealed record SampleEntity(string Name, DateTimeOffset CreatedAt, Guid Id);

    [Fact]
    public void Construction_captures_field_name_and_key_selector()
    {
        var spec = new SortSpec<SampleEntity>("name", x => x.Name);

        spec.FieldName.Should().Be("name");
        spec.KeySelector.Compile().Invoke(new SampleEntity("x", DateTimeOffset.UtcNow, Guid.NewGuid()))
            .Should().Be("x");
    }

    [Fact]
    public void Two_specs_with_different_lambda_instances_are_not_value_equal()
    {
        var a = new SortSpec<SampleEntity>("name", x => x.Name);
        var b = new SortSpec<SampleEntity>("name", x => x.Name);

        // SortSpec is a record, but its KeySelector is an Expression<> with reference-only equality.
        // Two specs with the same field name but distinct lambda literals are NOT value-equal.
        // Callers MUST treat SortSpec by FieldName, not by record equality. ADR-0095 §5.
        a.Should().NotBe(b, "Expression<> instances do not implement structural equality");
        a.FieldName.Should().Be(b.FieldName);
    }

    [Fact]
    public void CompiledKeySelector_caches_the_delegate_across_accesses()
    {
        // Kills mutant at line 24: `_compiled ??= KeySelector.Compile()` mutated to `_compiled = KeySelector.Compile()`.
        // With original ??= the first access compiles once and caches; subsequent accesses return the same instance.
        // With mutated =, every access recompiles, producing a new delegate instance → BeSameAs fails.
        var spec = new SortSpec<SampleEntity>("name", x => x.Name);

        var first = spec.CompiledKeySelector;
        var second = spec.CompiledKeySelector;
        var third = spec.CompiledKeySelector;

        first.Should().BeSameAs(second);
        second.Should().BeSameAs(third);
    }
}
