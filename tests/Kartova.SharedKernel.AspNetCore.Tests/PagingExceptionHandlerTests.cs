using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Pagination;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public sealed class PagingExceptionHandlerTests
{
    [TestMethod]
    public async Task InvalidSortFieldException_maps_to_400_with_allowed_fields()
    {
        var (handler, ctx) = Build();
        var ex = new InvalidSortFieldException("foo", new[] { "createdAt", "name" });

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        StringAssert.StartsWith(ctx.Response.ContentType, "application/problem+json");

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        StringAssert.Contains(body, ProblemTypes.InvalidSortField);
        StringAssert.Contains(body, "\"createdAt\"");
        StringAssert.Contains(body, "\"name\"");
        StringAssert.Contains(body, "\"fieldName\"");
        StringAssert.Contains(body, "\"foo\"");
    }

    [TestMethod]
    public async Task InvalidCursorException_maps_to_400()
    {
        var (handler, ctx) = Build();
        var ex = new InvalidCursorException("Cursor JSON is malformed.");

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        StringAssert.Contains(body, ProblemTypes.InvalidCursor);
    }

    [TestMethod]
    public async Task InvalidLimitException_maps_to_400_invalid_limit()
    {
        var (handler, ctx) = Build();
        var ex = new InvalidLimitException(limit: 0, minLimit: 1, maxLimit: 200);

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        StringAssert.Contains(body, ProblemTypes.InvalidLimit);
        StringAssert.Contains(body, "\"limit\"");
        StringAssert.Contains(body, "\"minLimit\"");
        StringAssert.Contains(body, "\"maxLimit\"");
    }

    [TestMethod]
    public async Task InvalidSortOrderException_maps_to_400_with_value()
    {
        var (handler, ctx) = Build();
        var ex = new InvalidSortOrderException("upward");

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        StringAssert.Contains(body, ProblemTypes.InvalidSortOrder);
        StringAssert.Contains(body, "\"value\"");
        StringAssert.Contains(body, "\"upward\"");
    }

    [TestMethod]
    public async Task CursorFilterMismatchException_maps_to_400_with_filter_extensions()
    {
        var (handler, ctx) = Build();
        var ex = new CursorFilterMismatchException("includeDecommissioned", "true", "false");

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        StringAssert.StartsWith(ctx.Response.ContentType, "application/problem+json");

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        StringAssert.Contains(body, ProblemTypes.CursorFilterMismatch);
        StringAssert.Contains(body, "\"filterName\"");
        StringAssert.Contains(body, "\"includeDecommissioned\"");
        StringAssert.Contains(body, "\"expectedValue\"");
        StringAssert.Contains(body, "\"true\"");
        StringAssert.Contains(body, "\"actualValue\"");
        StringAssert.Contains(body, "\"false\"");
    }

    [TestMethod]
    public async Task UnrelatedException_returns_false()
    {
        var (handler, ctx) = Build();

        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException("x"), CancellationToken.None);

        Assert.IsFalse(handled);
    }

    private static (PagingExceptionHandler handler, HttpContext ctx) Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var sp = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Response.Body = new MemoryStream();

        var handler = new PagingExceptionHandler(
            sp.GetRequiredService<IProblemDetailsService>());
        return (handler, ctx);
    }
}
