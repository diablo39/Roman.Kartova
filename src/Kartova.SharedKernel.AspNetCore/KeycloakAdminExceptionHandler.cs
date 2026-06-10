using Kartova.SharedKernel.Identity;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps an uncaught <see cref="KeycloakAdminException"/> (raised when a handler's call to the
/// KeyCloak Admin REST API fails) to RFC 7807 <c>502 Bad Gateway</c> with
/// <c>type = </c><see cref="ProblemTypes.ServiceUnavailable"/>. Slice-10 Task 6 Part D.
///
/// <para>
/// Handlers that call KeyCloak as part of a write (<c>ChangeMemberRoleHandler</c>,
/// <c>OffboardMemberHandler</c>) deliberately do NOT catch this exception: letting it propagate
/// aborts the request so the ambient <c>ITenantScope</c> transaction rolls back any partial DB work
/// (ADR-0090). Without this handler the exception would fall through to a generic 500; mapping it to
/// a typed 502 tells clients the failure is an upstream identity-provider problem (retryable) rather
/// than a server bug. The detail is intentionally generic so KeyCloak internals never leak.
/// </para>
///
/// <para>
/// This handler only fires on <see cref="KeycloakAdminException"/> instances that escape a handler.
/// <c>CreateInvitationHandler</c> catches its own KC failures locally (compensation), so this global
/// handler never sees those — its behavior is unaffected.
/// </para>
/// </summary>
public sealed class KeycloakAdminExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not KeycloakAdminException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.ServiceUnavailable,
            Title = "Identity provider unavailable",
            Status = StatusCodes.Status502BadGateway,
            Detail = "The identity provider could not complete the request. Please retry shortly.",
        };

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
