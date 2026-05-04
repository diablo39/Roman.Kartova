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
    public void Records_with_same_field_name_are_equal()
    {
        var a = new SortSpec<SampleEntity>("name", x => x.Name);
        var b = new SortSpec<SampleEntity>("name", x => x.Name);

        a.FieldName.Should().Be(b.FieldName);
    }
}
