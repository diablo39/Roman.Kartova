using System.Security.Claims;
using System.Text.RegularExpressions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public class HttpContextCurrentUserTests
{
    [TestMethod]
    public void UserId_returns_guid_parsed_from_sub_claim()
    {
        var expected = Guid.NewGuid();
        var sut = CreateSut(("sub", expected.ToString()));

        Assert.AreEqual(expected, sut.UserId);
    }

    [TestMethod]
    public void UserId_throws_when_sub_claim_missing()
    {
        var sut = CreateSut();

        // Tightening: original FA `Should().Throw<InvalidOperationException>()` allowed derived
        // types; ThrowsExactly enforces exact type. The handler raises plain InvalidOperationException.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _ = sut.UserId);
        StringAssert.Matches(ex.Message, new Regex(".*sub.*"));
    }

    [TestMethod]
    public void UserId_throws_when_sub_claim_is_not_a_guid()
    {
        var sut = CreateSut(("sub", "not-a-guid"));

        // Tightening: ThrowsExactly enforces exact FormatException type vs FA's loose Throw.
        Assert.ThrowsExactly<FormatException>(() => _ = sut.UserId);
    }

    private static HttpContextCurrentUser CreateSut(params (string Type, string Value)[] claims)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                claims.Select(c => new Claim(c.Type, c.Value)),
                "test")),
        };
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return new HttpContextCurrentUser(accessor);
    }
}
