using FluentAssertions;
using Kartova.SharedKernel.Pagination;
using Xunit;

namespace Kartova.SharedKernel.Tests.Pagination;

public class CursorFilterMismatchExceptionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_throws_ArgumentException_when_filterName_is_null_empty_or_whitespace(string? filterName)
    {
        var act = () => new CursorFilterMismatchException(filterName!, "true", "false");
        act.Should().Throw<ArgumentException>().WithParameterName("filterName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_throws_ArgumentException_when_expectedValue_is_null_empty_or_whitespace(string? expectedValue)
    {
        var act = () => new CursorFilterMismatchException("includeDecommissioned", expectedValue!, "false");
        act.Should().Throw<ArgumentException>().WithParameterName("expectedValue");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_throws_ArgumentException_when_actualValue_is_null_empty_or_whitespace(string? actualValue)
    {
        var act = () => new CursorFilterMismatchException("includeDecommissioned", "true", actualValue!);
        act.Should().Throw<ArgumentException>().WithParameterName("actualValue");
    }

    [Fact]
    public void Ctor_with_valid_args_sets_all_properties_and_message()
    {
        var ex = new CursorFilterMismatchException("includeDecommissioned", "true", "false");
        ex.FilterName.Should().Be("includeDecommissioned");
        ex.ExpectedValue.Should().Be("true");
        ex.ActualValue.Should().Be("false");
        ex.Message.Should().Be("Cursor was issued for includeDecommissioned=true but request uses includeDecommissioned=false.");
    }
}
