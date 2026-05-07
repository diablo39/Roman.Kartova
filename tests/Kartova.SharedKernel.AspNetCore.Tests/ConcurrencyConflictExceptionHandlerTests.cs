using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using NSubstitute;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class ConcurrencyConflictExceptionHandlerTests
{
    [Fact]
    public async Task Maps_DbUpdateConcurrencyException_to_412_with_correct_type()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new ConcurrencyConflictExceptionHandler(pds);
        var http = new DefaultHttpContext();

        // Construct the exception with no entries — the handler still produces
        // the 412 envelope, just without the currentVersion extension.
        // EF Core 10's public ctor takes IReadOnlyList<IUpdateEntry>; the
        // EntityEntry overload exists only on DbUpdateException, not the
        // concurrency subclass. The "real" currentVersion path is exercised
        // by the integration test in Task 11 (full Postgres roundtrip).
        var ex = new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>());

        var handled = await handler.TryHandleAsync(http, ex, CancellationToken.None);

        handled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Type == ProblemTypes.ConcurrencyConflict &&
            c.ProblemDetails.Status == 412));
    }

    [Fact]
    public async Task Returns_false_for_unrelated_exception()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        var handler = new ConcurrencyConflictExceptionHandler(pds);

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(),
            new InvalidOperationException(), CancellationToken.None);

        handled.Should().BeFalse();
        await pds.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
