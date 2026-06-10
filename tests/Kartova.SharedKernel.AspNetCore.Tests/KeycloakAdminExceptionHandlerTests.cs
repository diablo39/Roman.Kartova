using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

/// <summary>
/// Unit tests for <see cref="KeycloakAdminExceptionHandler"/> (slice-10 Task 6 Part D).
/// Asserts the 502 mapping for <see cref="KeycloakAdminException"/> and the pass-through
/// (returns false, writes nothing) for unrelated exceptions.
/// </summary>
[TestClass]
public class KeycloakAdminExceptionHandlerTests
{
    [TestMethod]
    public async Task Maps_KeycloakAdminException_to_502_service_unavailable()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new KeycloakAdminExceptionHandler(pds);
        var http = new DefaultHttpContext();

        var ex = new KeycloakAdminException(KeycloakAdminError.Unexpected, "KC unreachable");

        var handled = await handler.TryHandleAsync(http, ex, CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status502BadGateway, http.Response.StatusCode);
        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Type == ProblemTypes.ServiceUnavailable &&
            c.ProblemDetails.Status == StatusCodes.Status502BadGateway &&
            c.ProblemDetails.Title == "Identity provider unavailable"));
    }

    [TestMethod]
    public async Task Detail_does_not_leak_keycloak_internals()
    {
        // The KC exception message ("secret realm internal detail") must NOT appear in the
        // surfaced ProblemDetails.Detail — the handler emits a generic, fixed detail string.
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new KeycloakAdminExceptionHandler(pds);
        var http = new DefaultHttpContext();

        var ex = new KeycloakAdminException(KeycloakAdminError.NotFound, "secret realm internal detail");

        await handler.TryHandleAsync(http, ex, CancellationToken.None);

        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Detail != null &&
            !c.ProblemDetails.Detail.Contains("secret realm internal detail")));
    }

    [TestMethod]
    public async Task Returns_false_for_unrelated_exception()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        var handler = new KeycloakAdminExceptionHandler(pds);
        var http = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(http,
            new InvalidOperationException(), CancellationToken.None);

        Assert.IsFalse(handled);
        // Status untouched (still the default 200) and nothing written.
        Assert.AreEqual(StatusCodes.Status200OK, http.Response.StatusCode);
        await pds.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
