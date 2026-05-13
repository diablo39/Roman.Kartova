using System.Text.RegularExpressions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public class IfMatchEndpointFilterTests
{
    [TestMethod]
    public async Task Throws_when_header_missing()
    {
        var ctx = MakeContext(headerValue: null);
        var filter = new IfMatchEndpointFilter();

        // Note: Assert.ThrowsExactlyAsync<PreconditionRequiredException> is used uniformly throughout
        // this file as a translation policy. Since PreconditionRequiredException is sealed, there is
        // no behavioural difference vs FluentAssertions' permissive Should().ThrowAsync<>() — but the
        // ThrowsExactly idiom is preferred per the migration's spec §4 to keep all exception
        // assertions strictly typed.
        var ex = await Assert.ThrowsExactlyAsync<PreconditionRequiredException>(
            async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null)));
        StringAssert.Matches(ex.Message, new Regex(".*required.*"));
    }

    [TestMethod]
    public async Task Throws_when_header_malformed()
    {
        var ctx = MakeContext(headerValue: "\"not-base64!\"");
        var filter = new IfMatchEndpointFilter();

        var ex = await Assert.ThrowsExactlyAsync<PreconditionRequiredException>(
            async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null)));
        StringAssert.Matches(ex.Message, new Regex(".*valid version.*"));
    }

    [TestMethod]
    public async Task Stores_decoded_version_in_HttpContext_Items_when_header_valid()
    {
        var encoded = VersionEncoding.Encode(42u);
        var ctx = MakeContext(headerValue: $"\"{encoded}\"");
        var filter = new IfMatchEndpointFilter();
        var nextCalled = false;

        await filter.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.FromResult<object?>(null); });

        Assert.IsTrue(nextCalled);
        Assert.AreEqual(42u, ctx.HttpContext.Items[IfMatchEndpointFilter.ExpectedVersionKey]);
    }

    [TestMethod]
    public async Task Accepts_unquoted_header_value()
    {
        var encoded = VersionEncoding.Encode(7u);
        var ctx = MakeContext(headerValue: encoded);                 // no quotes
        var filter = new IfMatchEndpointFilter();

        await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        Assert.AreEqual(7u, ctx.HttpContext.Items[IfMatchEndpointFilter.ExpectedVersionKey]);
    }

    [TestMethod]
    public async Task Throws_when_header_present_but_empty()
    {
        // Header key is present (TryGetValue=true) but the StringValues collection has Count==0.
        // Pins the FIRST throw site ("required") so the || -> && mutation is killed:
        // mutated code would fall through and throw the "valid version token" message instead.
        var ctx = MakeContextWithEmptyHeader();
        var filter = new IfMatchEndpointFilter();

        var ex = await Assert.ThrowsExactlyAsync<PreconditionRequiredException>(
            async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null)));
        Assert.AreEqual("If-Match header is required for this endpoint.", ex.Message);
    }

    [TestMethod]
    [DataRow("")]            // empty value
    [DataRow("   ")]         // whitespace-only
    public async Task Throws_required_for_empty_or_whitespace_value(string headerValue)
    {
        var ctx = MakeContext(headerValue);
        var filter = new IfMatchEndpointFilter();

        var ex = await Assert.ThrowsExactlyAsync<PreconditionRequiredException>(
            async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null)));
        Assert.AreEqual("If-Match header is required for this endpoint.", ex.Message);
    }

    [TestMethod]
    public async Task Throws_with_wildcard_specific_message_for_star()
    {
        var ctx = MakeContext("*");
        var filter = new IfMatchEndpointFilter();

        var ex = await Assert.ThrowsExactlyAsync<PreconditionRequiredException>(
            async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null)));
        StringAssert.Matches(ex.Message, new Regex(".*wildcard.*not supported.*"));
    }

    [TestMethod]
    [DataRow("W/\"abc\"")]
    [DataRow("W/\"\"")]
    public async Task Throws_with_weak_etag_specific_message(string headerValue)
    {
        var ctx = MakeContext(headerValue);
        var filter = new IfMatchEndpointFilter();

        var ex = await Assert.ThrowsExactlyAsync<PreconditionRequiredException>(
            async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null)));
        StringAssert.Matches(ex.Message, new Regex(".*Weak ETags.*not supported.*"));
    }

    [TestMethod]
    public async Task Throws_with_list_specific_message_for_comma_separated_etags()
    {
        // RFC 7232 allows If-Match: "v1", "v2" — the StringValues form joins with comma.
        var v1 = VersionEncoding.Encode(1u);
        var v2 = VersionEncoding.Encode(2u);
        var ctx = MakeContext($"\"{v1}\", \"{v2}\"");
        var filter = new IfMatchEndpointFilter();

        var ex = await Assert.ThrowsExactlyAsync<PreconditionRequiredException>(
            async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null)));
        StringAssert.Matches(ex.Message, new Regex(".*list.*not supported.*"));
    }

    [TestMethod]
    [DataRow("\"abc")]    // leading-only quote
    [DataRow("abc\"")]    // trailing-only quote
    [DataRow("ab\"cd")]   // embedded quote
    public async Task Throws_invalid_for_mismatched_quotes(string headerValue)
    {
        var ctx = MakeContext(headerValue);
        var filter = new IfMatchEndpointFilter();

        var ex = await Assert.ThrowsExactlyAsync<PreconditionRequiredException>(
            async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null)));
        StringAssert.Matches(ex.Message, new Regex(".*valid version token.*"));
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
