using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class IfMatchEndpointFilterTests
{
    [Fact]
    public async Task Throws_when_header_missing()
    {
        var ctx = MakeContext(headerValue: null);
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>().WithMessage("*required*");
    }

    [Fact]
    public async Task Throws_when_header_malformed()
    {
        var ctx = MakeContext(headerValue: "\"not-base64!\"");
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>().WithMessage("*valid version*");
    }

    [Fact]
    public async Task Stores_decoded_version_in_HttpContext_Items_when_header_valid()
    {
        var encoded = VersionEncoding.Encode(42u);
        var ctx = MakeContext(headerValue: $"\"{encoded}\"");
        var filter = new IfMatchEndpointFilter();
        var nextCalled = false;

        await filter.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.FromResult<object?>(null); });

        nextCalled.Should().BeTrue();
        ctx.HttpContext.Items[IfMatchEndpointFilter.ExpectedVersionKey].Should().Be(42u);
    }

    [Fact]
    public async Task Accepts_unquoted_header_value()
    {
        var encoded = VersionEncoding.Encode(7u);
        var ctx = MakeContext(headerValue: encoded);                 // no quotes
        var filter = new IfMatchEndpointFilter();

        await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        ctx.HttpContext.Items[IfMatchEndpointFilter.ExpectedVersionKey].Should().Be(7u);
    }

    private static EndpointFilterInvocationContext MakeContext(string? headerValue)
    {
        var http = new DefaultHttpContext();
        if (headerValue is not null)
        {
            http.Request.Headers["If-Match"] = headerValue;
        }
        return new DefaultEndpointFilterInvocationContext(http);
    }
}
