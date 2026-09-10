using Kartova.SharedKernel.Pagination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests.Pagination;

[TestClass]
public class CursorSortFieldMismatchExceptionTests
{
    // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null and
    // ArgumentException for empty/whitespace; catch as the base class and assert ParamName.
    // Mirrors CursorFilterMismatchExceptionTests.
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
    public void Ctor_throws_ArgumentException_when_expectedField_is_null_empty_or_whitespace(string? expectedField)
    {
        var ex = CaptureArgumentExceptionOrDerived(
            () => new CursorSortFieldMismatchException(expectedField!, "createdAt"));
        Assert.AreEqual("expectedField", ex.ParamName);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Ctor_throws_ArgumentException_when_actualField_is_null_empty_or_whitespace(string? actualField)
    {
        var ex = CaptureArgumentExceptionOrDerived(
            () => new CursorSortFieldMismatchException("name", actualField!));
        Assert.AreEqual("actualField", ex.ParamName);
    }

    [TestMethod]
    public void Ctor_with_valid_args_sets_all_properties_and_message()
    {
        var ex = new CursorSortFieldMismatchException("name", "createdAt");
        Assert.AreEqual("name", ex.ExpectedField);
        Assert.AreEqual("createdAt", ex.ActualField);
        Assert.AreEqual(
            "Cursor was issued for sortBy=name but request uses sortBy=createdAt.",
            ex.Message);
    }
}
