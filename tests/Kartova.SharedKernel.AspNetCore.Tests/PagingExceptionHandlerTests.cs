using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Pagination;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public sealed class PagingExceptionHandlerTests
{
    [Fact]
    public async Task InvalidSortFieldException_maps_to_400_with_allowed_fields()
    {
        var handler = new PagingExceptionHandler();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var ex = new InvalidSortFieldException("foo", new[] { "createdAt", "name" });

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.ContentType.Should().StartWith("application/problem+json");

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain(ProblemTypes.InvalidSortField);
        body.Should().Contain("createdAt");
        body.Should().Contain("name");
    }

    [Fact]
    public async Task InvalidCursorException_maps_to_400()
    {
        var handler = new PagingExceptionHandler();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var ex = new InvalidCursorException("Cursor JSON is malformed.");

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain(ProblemTypes.InvalidCursor);
    }

    [Fact]
    public async Task UnrelatedException_returns_false()
    {
        var handler = new PagingExceptionHandler();
        var ctx = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException("x"), CancellationToken.None);

        handled.Should().BeFalse();
    }
}
