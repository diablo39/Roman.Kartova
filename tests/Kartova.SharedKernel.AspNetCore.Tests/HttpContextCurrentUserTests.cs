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

        // Translation policy: Assert.ThrowsExactly is used uniformly per spec §4. Production throws
        // via literal `new InvalidOperationException(...)`, so there is no behavioural difference vs
        // FluentAssertions' permissive Should().Throw<>().
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _ = sut.UserId);
        StringAssert.Matches(ex.Message, new Regex(".*sub.*"));
    }

    [TestMethod]
    public void UserId_throws_when_sub_claim_is_not_a_guid()
    {
        var sut = CreateSut(("sub", "not-a-guid"));

        // Translation policy per spec §4: Guid.Parse raises FormatException directly per BCL contract,
        // so ThrowsExactly is exact-type by construction with no derived-type narrowing.
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
