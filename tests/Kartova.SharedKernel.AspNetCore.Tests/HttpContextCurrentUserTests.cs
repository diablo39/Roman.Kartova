using System.Security.Claims;
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class HttpContextCurrentUserTests
{
    [Fact]
    public void UserId_returns_guid_parsed_from_sub_claim()
    {
        var expected = Guid.NewGuid();
        var sut = CreateSut(("sub", expected.ToString()));

        sut.UserId.Should().Be(expected);
    }

    [Fact]
    public void UserId_throws_when_sub_claim_missing()
    {
        var sut = CreateSut();

        var act = () => _ = sut.UserId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sub*");
    }

    [Fact]
    public void UserId_throws_when_sub_claim_is_not_a_guid()
    {
        var sut = CreateSut(("sub", "not-a-guid"));

        var act = () => _ = sut.UserId;

        act.Should().Throw<FormatException>();
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
