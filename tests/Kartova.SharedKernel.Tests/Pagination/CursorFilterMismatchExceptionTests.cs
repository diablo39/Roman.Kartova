using Kartova.SharedKernel.Pagination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests.Pagination;

[TestClass]
public class CursorFilterMismatchExceptionTests
{
    // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
    // and ArgumentException for empty/whitespace. The original FA `Throw<ArgumentException>()`
    // tolerated both via base-type matching; MSTest's ThrowsExactly is type-strict, so we
    // catch as the base class and assert ParamName.
    private static ArgumentException CaptureArgumentExceptionOrDerived(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException ex)
        {
            return ex;
        }
        Assert.Fail("Expected ArgumentException (or derived) was not thrown.");
        return null!; // unreachable
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Ctor_throws_ArgumentException_when_filterName_is_null_empty_or_whitespace(string? filterName)
    {
        var ex = CaptureArgumentExceptionOrDerived(
            () => new CursorFilterMismatchException(filterName!, "true", "false"));
        Assert.AreEqual("filterName", ex.ParamName);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Ctor_throws_ArgumentException_when_expectedValue_is_null_empty_or_whitespace(string? expectedValue)
    {
        var ex = CaptureArgumentExceptionOrDerived(
            () => new CursorFilterMismatchException("includeDecommissioned", expectedValue!, "false"));
        Assert.AreEqual("expectedValue", ex.ParamName);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Ctor_throws_ArgumentException_when_actualValue_is_null_empty_or_whitespace(string? actualValue)
    {
        var ex = CaptureArgumentExceptionOrDerived(
            () => new CursorFilterMismatchException("includeDecommissioned", "true", actualValue!));
        Assert.AreEqual("actualValue", ex.ParamName);
    }

    [TestMethod]
    public void Ctor_with_valid_args_sets_all_properties_and_message()
    {
        var ex = new CursorFilterMismatchException("includeDecommissioned", "true", "false");
        Assert.AreEqual("includeDecommissioned", ex.FilterName);
        Assert.AreEqual("true", ex.ExpectedValue);
        Assert.AreEqual("false", ex.ActualValue);
        Assert.AreEqual(
            "Cursor was issued for includeDecommissioned=true but request uses includeDecommissioned=false.",
            ex.Message);
    }
}
