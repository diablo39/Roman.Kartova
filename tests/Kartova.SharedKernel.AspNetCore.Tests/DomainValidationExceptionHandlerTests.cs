using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

/// <summary>
/// Pinning tests for slice-3 spec §13.3 — domain validation exceptions surface
/// as RFC 7807 400 via the global exception-handler chain, not per-endpoint
/// try/catch.
/// </summary>
public class DomainValidationExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_maps_ArgumentException_to_400_problem_details()
    {
        var (sut, ctx) = Build();

        var handled = await sut.TryHandleAsync(
            ctx, new ArgumentException("name must not be empty"), CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task TryHandleAsync_maps_ArgumentNullException_to_400_problem_details()
    {
        // ArgumentNullException : ArgumentException — should be caught by the same handler.
        var (sut, ctx) = Build();

        var handled = await sut.TryHandleAsync(
            ctx, new ArgumentNullException("name"), CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task TryHandleAsync_returns_false_for_unrelated_exceptions()
    {
        // Non-ArgumentException must fall through so UseExceptionHandler emits 500.
        var (sut, ctx) = Build();

        var handled = await sut.TryHandleAsync(
            ctx, new InvalidOperationException("oops"), CancellationToken.None);

        handled.Should().BeFalse();
    }

    private static (DomainValidationExceptionHandler sut, HttpContext ctx) Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var sp = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Response.Body = new MemoryStream();

        var sut = new DomainValidationExceptionHandler(
            sp.GetRequiredService<IProblemDetailsService>());
        return (sut, ctx);
    }
}
