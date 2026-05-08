using System.Text.Json;
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Pagination;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public sealed class PagingExceptionHandlerTests
{
    [Fact]
    public async Task InvalidSortFieldException_maps_to_400_with_allowed_fields()
    {
        var (handler, ctx) = Build();
        var ex = new InvalidSortFieldException("foo", new[] { "createdAt", "name" });

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.ContentType.Should().StartWith("application/problem+json");

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain(ProblemTypes.InvalidSortField);
        body.Should().Contain("\"createdAt\"");
        body.Should().Contain("\"name\"");
        body.Should().Contain("\"fieldName\"");
        body.Should().Contain("\"foo\"");
    }

    [Fact]
    public async Task InvalidCursorException_maps_to_400()
    {
        var (handler, ctx) = Build();
        var ex = new InvalidCursorException("Cursor JSON is malformed.");

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain(ProblemTypes.InvalidCursor);
    }

    [Fact]
    public async Task InvalidLimitException_maps_to_400_invalid_limit()
    {
        var (handler, ctx) = Build();
        var ex = new InvalidLimitException(limit: 0, minLimit: 1, maxLimit: 200);

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain(ProblemTypes.InvalidLimit);
        body.Should().Contain("\"limit\"");
        body.Should().Contain("\"minLimit\"");
        body.Should().Contain("\"maxLimit\"");
    }

    [Fact]
    public async Task InvalidSortOrderException_maps_to_400_with_value()
    {
        var (handler, ctx) = Build();
        var ex = new InvalidSortOrderException("upward");

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain(ProblemTypes.InvalidSortOrder);
        body.Should().Contain("\"value\"");
        body.Should().Contain("\"upward\"");
    }

    [Fact]
    public async Task CursorFilterMismatchException_maps_to_400_with_filter_extensions()
    {
        var (handler, ctx) = Build();
        var ex = new CursorFilterMismatchException("includeDecommissioned", "true", "false");

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.ContentType.Should().StartWith("application/problem+json");

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain(ProblemTypes.CursorFilterMismatch);
        body.Should().Contain("\"filterName\"");
        body.Should().Contain("\"includeDecommissioned\"");
        body.Should().Contain("\"expectedValue\"");
        body.Should().Contain("\"true\"");
        body.Should().Contain("\"actualValue\"");
        body.Should().Contain("\"false\"");
    }

    [Fact]
    public async Task UnrelatedException_returns_false()
    {
        var (handler, ctx) = Build();

        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException("x"), CancellationToken.None);

        handled.Should().BeFalse();
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
