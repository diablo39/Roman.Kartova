using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
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

    [Fact]
    public async Task Throws_when_header_present_but_empty()
    {
        // Header key is present (TryGetValue=true) but the StringValues collection has Count==0.
        // Pins the FIRST throw site ("required") so the || -> && mutation is killed:
        // mutated code would fall through and throw the "valid version token" message instead.
        var ctx = MakeContextWithEmptyHeader();
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>()
            .WithMessage("If-Match header is required for this endpoint.");
    }

    [Theory]
    [InlineData("")]            // empty value
    [InlineData("   ")]         // whitespace-only
    public async Task Throws_required_for_empty_or_whitespace_value(string headerValue)
    {
        var ctx = MakeContext(headerValue);
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>()
            .WithMessage("If-Match header is required for this endpoint.");
    }

    [Fact]
    public async Task Throws_with_wildcard_specific_message_for_star()
    {
        var ctx = MakeContext("*");
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>()
            .WithMessage("*wildcard*not supported*");
    }

    [Theory]
    [InlineData("W/\"abc\"")]
    [InlineData("W/\"\"")]
    public async Task Throws_with_weak_etag_specific_message(string headerValue)
    {
        var ctx = MakeContext(headerValue);
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>()
            .WithMessage("*Weak ETags*not supported*");
    }

    [Fact]
    public async Task Throws_with_list_specific_message_for_comma_separated_etags()
    {
        // RFC 7232 allows If-Match: "v1", "v2" — the StringValues form joins with comma.
        var v1 = VersionEncoding.Encode(1u);
        var v2 = VersionEncoding.Encode(2u);
        var ctx = MakeContext($"\"{v1}\", \"{v2}\"");
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>()
            .WithMessage("*list*not supported*");
    }

    [Theory]
    [InlineData("\"abc")]    // leading-only quote
    [InlineData("abc\"")]    // trailing-only quote
    [InlineData("ab\"cd")]   // embedded quote
    public async Task Throws_invalid_for_mismatched_quotes(string headerValue)
    {
        var ctx = MakeContext(headerValue);
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>()
            .WithMessage("*valid version token*");
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

    private static EndpointFilterInvocationContext MakeContextWithEmptyHeader()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["If-Match"] = StringValues.Empty;
        return new DefaultEndpointFilterInvocationContext(http);
    }
}
