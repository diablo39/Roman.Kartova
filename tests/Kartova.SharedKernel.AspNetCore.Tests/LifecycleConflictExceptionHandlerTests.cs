using FluentAssertions;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NSubstitute;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class LifecycleConflictExceptionHandlerTests
{
    [Fact]
    public async Task Maps_to_409_with_currentLifecycle_and_attemptedTransition_extensions()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new LifecycleConflictExceptionHandler(pds);
        var http = new DefaultHttpContext();

        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Decommissioned, "Deprecate");

        var handled = await handler.TryHandleAsync(http, ex, CancellationToken.None);

        handled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Type == ProblemTypes.LifecycleConflict &&
            (string)c.ProblemDetails.Extensions["currentLifecycle"]! == "Decommissioned" &&
            (string)c.ProblemDetails.Extensions["attemptedTransition"]! == "Deprecate"));
    }

    [Fact]
    public async Task Includes_sunsetDate_and_reason_when_provided()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new LifecycleConflictExceptionHandler(pds);
        var http = new DefaultHttpContext();
        var sunset = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Deprecated, "Decommission", sunset, "before-sunset-date");

        await handler.TryHandleAsync(http, ex, CancellationToken.None);

        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Extensions.ContainsKey("sunsetDate") &&
            (string)c.ProblemDetails.Extensions["reason"]! == "before-sunset-date"));
    }

    [Fact]
    public async Task Returns_false_for_unrelated_exception()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        var handler = new LifecycleConflictExceptionHandler(pds);

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(),
            new InvalidOperationException(), CancellationToken.None);

        handled.Should().BeFalse();
        await pds.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
