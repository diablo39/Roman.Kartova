using System.Security.Claims;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public sealed class HttpContextCurrentUserDisplayNameTests
{
    private static HttpContextCurrentUser Build(params Claim[] claims)
    {
        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        });
        return new HttpContextCurrentUser(http, Substitute.For<ITenantContext>());
    }

    [TestMethod]
    public void DisplayName_PrefersNameClaim()
        => Assert.AreEqual("Ada Lovelace", Build(
            new Claim("sub", "s"), new Claim("name", "Ada Lovelace"),
            new Claim("preferred_username", "ada"), new Claim("email", "ada@x.io")).DisplayName);

    [TestMethod]
    public void DisplayName_FallsBackToPreferredUsername()
        => Assert.AreEqual("ada", Build(
            new Claim("sub", "s"), new Claim("preferred_username", "ada"),
            new Claim("email", "ada@x.io")).DisplayName);

    [TestMethod]
    public void DisplayName_FallsBackToEmail()
        => Assert.AreEqual("ada@x.io", Build(
            new Claim("sub", "s"), new Claim("email", "ada@x.io")).DisplayName);

    [TestMethod]
    public void DisplayName_FallsBackToSub()
        => Assert.AreEqual("s", Build(new Claim("sub", "s")).DisplayName);

    [TestMethod]
    public void DisplayName_ThrowsWhenHttpContextIsNull()
    {
        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpContextCurrentUser(http, Substitute.For<ITenantContext>());

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _ = sut.DisplayName);
        Assert.AreEqual("No HttpContext on current request.", ex.Message);
    }
}
