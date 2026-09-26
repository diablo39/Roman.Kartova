using System.Security.Claims;
using Kartova.Organization.Contracts;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kartova.Organization.Infrastructure.Tests;

[TestClass]
public sealed class AdminSessionEndpointDelegatesTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), authenticationType: "PlatformAdmin"));

    [TestMethod]
    public void GetMe_maps_sub_email_and_name()
    {
        var id = Guid.NewGuid();
        var result = AdminSessionEndpointDelegates.GetMe(
            Principal(("sub", id.ToString()), ("email", "op@kartova.local"), ("name", "Op Erator")));

        var ok = result as Ok<AdminMeResponse>;
        Assert.IsNotNull(ok, $"expected 200 Ok<AdminMeResponse>, got {result.GetType().Name}");
        Assert.AreEqual(new AdminMeResponse(id, "op@kartova.local", "Op Erator"), ok.Value);
    }

    [TestMethod]
    public void GetMe_falls_back_to_email_when_name_absent()
    {
        var id = Guid.NewGuid();
        var result = AdminSessionEndpointDelegates.GetMe(Principal(("sub", id.ToString()), ("email", "op@kartova.local")));

        Assert.AreEqual("op@kartova.local", ((Ok<AdminMeResponse>)result).Value!.DisplayName);
    }

    [TestMethod]
    public void GetMe_returns_empty_email_and_display_name_when_both_absent()
    {
        // A realm without the email scope must not 500; the console shows an empty label.
        var result = AdminSessionEndpointDelegates.GetMe(Principal(("sub", Guid.NewGuid().ToString())));

        var value = ((Ok<AdminMeResponse>)result).Value!;
        Assert.AreEqual(string.Empty, value.Email);
        Assert.AreEqual(string.Empty, value.DisplayName);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not-a-guid")]
    public void GetMe_returns_401_invalid_token_when_sub_missing_or_not_a_guid(string? sub)
    {
        var principal = sub is null ? Principal(("email", "op@kartova.local")) : Principal(("sub", sub));

        var result = AdminSessionEndpointDelegates.GetMe(principal);

        var problem = result as ProblemHttpResult;
        Assert.IsNotNull(problem, $"expected ProblemHttpResult, got {result.GetType().Name}");
        Assert.AreEqual(StatusCodes.Status401Unauthorized, problem.StatusCode);
        Assert.AreEqual(ProblemTypes.InvalidToken, problem.ProblemDetails.Type);
    }
}
