using System.Text.Json;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

/// <summary>
/// Pinning tests for slice-3 spec §13.3 — domain validation exceptions surface
/// as RFC 7807 400 via the global exception-handler chain, not per-endpoint
/// try/catch.
/// </summary>
[TestClass]
public class DomainValidationExceptionHandlerTests
{
    [TestMethod]
    public async Task TryHandleAsync_maps_ArgumentException_to_400_problem_details()
    {
        var (sut, ctx) = Build();

        var handled = await sut.TryHandleAsync(
            ctx, new ArgumentException("name must not be empty"), CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);

        var body = await ReadBodyAsync(ctx);
        Assert.AreEqual(ProblemTypes.ValidationFailed, body.GetProperty("type").GetString());
        Assert.AreEqual("Invalid request", body.GetProperty("title").GetString());
        Assert.AreEqual(StatusCodes.Status400BadRequest, body.GetProperty("status").GetInt32());
        Assert.AreEqual("name must not be empty", body.GetProperty("detail").GetString());
    }

    [TestMethod]
    public async Task TryHandleAsync_maps_ArgumentNullException_to_400_problem_details()
    {
        // ArgumentNullException : ArgumentException — should be caught by the same handler.
        var (sut, ctx) = Build();

        var handled = await sut.TryHandleAsync(
            ctx, new ArgumentNullException("name"), CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task TryHandleAsync_strips_paramName_suffix_for_ArgumentNullException()
    {
        // ArgumentNullException.Message is "Value cannot be null. (Parameter 'X')".
        // The errors[name] entry must not contain the framework suffix.
        var (sut, ctx) = Build();

        await sut.TryHandleAsync(
            ctx, new ArgumentNullException("name"), CancellationToken.None);

        var body = await ReadBodyAsync(ctx);
        var nameError = body.GetProperty("errors").GetProperty("name")
            .EnumerateArray().Single().GetString();
        // The AreEqual subsumes the IsFalse check, but we keep the explicit absence assert
        // for diagnostic clarity if the equality ever fails on a regression.
        Assert.IsFalse(nameError!.Contains("(Parameter"));
        Assert.AreEqual("Value cannot be null.", nameError);
    }

    [TestMethod]
    public async Task TryHandleAsync_returns_false_for_unrelated_exceptions()
    {
        // Non-ArgumentException must fall through so UseExceptionHandler emits 500.
        var (sut, ctx) = Build();

        var handled = await sut.TryHandleAsync(
            ctx, new InvalidOperationException("oops"), CancellationToken.None);

        Assert.IsFalse(handled);
    }

    [TestMethod]
    public async Task TryHandleAsync_emits_field_level_errors_when_paramName_present()
    {
        var (sut, ctx) = Build();

        var handled = await sut.TryHandleAsync(
            ctx,
            new ArgumentException(
                "Application display name must not be empty.", "displayName"),
            CancellationToken.None);

        Assert.IsTrue(handled);
        var body = await ReadBodyAsync(ctx);

        var errors = body.GetProperty("errors");
        Assert.AreEqual(
            "Application display name must not be empty.",
            errors.GetProperty("displayName").EnumerateArray().Single().GetString());

        // Detail still carries the legacy single-message shape (with framework suffix)
        // for non-form consumers (CLI, agents).
        Assert.IsTrue(body.GetProperty("detail").GetString()!
            .Contains("Application display name must not be empty."));
    }

    [TestMethod]
    public async Task TryHandleAsync_omits_errors_property_when_paramName_absent()
    {
        // ArgumentException with no paramName → no field-level mapping possible;
        // SPA form receives the global toast instead of field highlight.
        var (sut, ctx) = Build();

        var handled = await sut.TryHandleAsync(
            ctx, new ArgumentException("something is off"), CancellationToken.None);

        Assert.IsTrue(handled);
        var body = await ReadBodyAsync(ctx);

        Assert.IsFalse(body.TryGetProperty("errors", out _));
    }

    private static (DomainValidationExceptionHandler sut, HttpContext ctx) Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var sp = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Response.Body = new MemoryStream();

        var sut = new DomainValidationExceptionHandler(
            sp.GetRequiredService<IProblemDetailsService>());
        return (sut, ctx);
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return doc.RootElement.Clone();
    }
}
