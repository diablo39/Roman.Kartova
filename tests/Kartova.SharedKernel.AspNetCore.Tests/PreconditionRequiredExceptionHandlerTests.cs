using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public class PreconditionRequiredExceptionHandlerTests
{
    [TestMethod]
    public async Task Maps_PreconditionRequiredException_to_428_with_correct_type()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new PreconditionRequiredExceptionHandler(pds);
        var http = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(http,
            new PreconditionRequiredException("If-Match required."),
            CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status428PreconditionRequired, http.Response.StatusCode);
        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Type == ProblemTypes.PreconditionRequired &&
            c.ProblemDetails.Status == 428));
    }

    [TestMethod]
    public async Task Returns_false_for_unrelated_exception()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        var handler = new PreconditionRequiredExceptionHandler(pds);

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(),
            new InvalidOperationException("nope"), CancellationToken.None);

        Assert.IsFalse(handled);
        await pds.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
