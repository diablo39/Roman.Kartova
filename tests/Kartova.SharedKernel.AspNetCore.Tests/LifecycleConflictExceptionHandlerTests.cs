using FluentAssertions;
using Kartova.SharedKernel;
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

        var ex = new FakeLifecycleConflict("decommissioned", "Deprecate");

        var handled = await handler.TryHandleAsync(http, ex, CancellationToken.None);

        handled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Type == ProblemTypes.LifecycleConflict &&
            (string)c.ProblemDetails.Extensions["currentLifecycle"]! == "decommissioned" &&
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

        var ex = new FakeLifecycleConflict(
            "deprecated", "Decommission", sunset, "before-sunset-date");

        await handler.TryHandleAsync(http, ex, CancellationToken.None);

        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Extensions.ContainsKey("sunsetDate") &&
            (string)c.ProblemDetails.Extensions["reason"]! == "before-sunset-date"));
    }

    [Fact]
    public async Task Omits_sunsetDate_and_reason_when_not_provided()
    {
        // Pins the absence-path: when ILifecycleConflict.SunsetDate / Reason are null,
        // the handler MUST NOT add the corresponding extension keys. Mutations on the
        // null-guards (e.g. removing the `if (... HasValue)` check) would survive
        // without this assertion.
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new LifecycleConflictExceptionHandler(pds);
        var http = new DefaultHttpContext();

        var ex = new FakeLifecycleConflict("active", "Decommission");

        await handler.TryHandleAsync(http, ex, CancellationToken.None);

        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            !c.ProblemDetails.Extensions.ContainsKey("sunsetDate") &&
            !c.ProblemDetails.Extensions.ContainsKey("reason")));
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

    // Local fake — keeps the handler test independent of any module's domain
    // exception. The handler matches by ILifecycleConflict (the whole point of
    // putting that interface in Kartova.SharedKernel was to break this exact
    // coupling). Using the Catalog-side InvalidLifecycleTransitionException
    // here would re-introduce the module reference the production design avoids.
    private sealed class FakeLifecycleConflict : Exception, ILifecycleConflict
    {
        public FakeLifecycleConflict(
            string currentLifecycleName,
            string attemptedTransition,
            DateTimeOffset? sunsetDate = null,
            string? reason = null)
            : base($"Cannot {attemptedTransition} from {currentLifecycleName}.")
        {
            CurrentLifecycleName = currentLifecycleName;
            AttemptedTransition = attemptedTransition;
            SunsetDate = sunsetDate;
            Reason = reason;
        }

        public string CurrentLifecycleName { get; }
        public string AttemptedTransition { get; }
        public DateTimeOffset? SunsetDate { get; }
        public string? Reason { get; }
    }
}
