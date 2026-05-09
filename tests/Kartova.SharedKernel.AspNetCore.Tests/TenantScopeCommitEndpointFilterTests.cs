using System.Text.RegularExpressions;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

/// <summary>
/// Pins the three branches of <see cref="TenantScopeCommitEndpointFilter"/>: happy
/// commit, missing-handle programmer-error, and commit-throws bubbling. The integration
/// suite covers the happy path implicitly through every authenticated request, but
/// the failure branches were uncovered (the slice-3 coverage run flagged the filter
/// at 60% line coverage).
/// </summary>
[TestClass]
public class TenantScopeCommitEndpointFilterTests
{
    [TestMethod]
    public async Task Invoke_commits_active_handle_then_returns_inner_result()
    {
        var handle = new RecordingHandle();
        var ctx = MakeContext(handle);
        var sut = new TenantScopeCommitEndpointFilter();

        var inner = Results.Ok("payload");
        var result = await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(inner));

        // The IResult is returned to ASP.NET so it can ExecuteAsync AFTER commit
        // succeeds — preserving ADR-0090 durability.
        Assert.AreSame(inner, result);
        // Exactly one commit per scoped request.
        Assert.AreEqual(1, handle.CommitCount);
    }

    [TestMethod]
    public async Task Invoke_throws_InvalidOperationException_when_handle_is_missing()
    {
        // No HandleKey on HttpContext.Items → indicates begin-middleware was not wired
        // before endpoint dispatch. Filter must surface this loudly rather than silently
        // skipping commit and leaving the transaction dangling.
        var ctx = MakeContext(handle: null);
        var sut = new TenantScopeCommitEndpointFilter();

        // Tightening: ThrowsExactlyAsync vs FA's loose ThrowAsync — exact type enforced.
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(Results.Ok())).AsTask());
        // Original FA chained two WithMessage calls — both substrings must appear.
        StringAssert.Matches(ex.Message, new Regex(".*TenantScopeCommitEndpointFilter ran without an active scope handle.*"));
        StringAssert.Matches(ex.Message, new Regex(".*TenantScopeBeginMiddleware.*"));
    }

    [TestMethod]
    public async Task Invoke_lets_commit_exception_bubble_to_UseExceptionHandler()
    {
        // ADR-0090 requires that a commit failure produce a 500 with no partial body —
        // the filter does this by simply not catching the exception, so UseExceptionHandler
        // and AddProblemDetails (ADR-0091) can map it to RFC 7807.
        var handle = new ThrowingHandle(new InvalidOperationException("connection lost"));
        var ctx = MakeContext(handle);
        var sut = new TenantScopeCommitEndpointFilter();

        // Tightening (see line 45): exact-type assertion vs FA's base-type permissiveness.
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(Results.Ok())).AsTask());
        Assert.AreEqual("connection lost", ex.Message);
    }

    private static FilterContextStub MakeContext(IAsyncTenantScopeHandle? handle)
    {
        var http = new DefaultHttpContext();
        if (handle is not null)
        {
            http.Items[TenantScopeBeginMiddleware.HandleKey] = handle;
        }
        return new FilterContextStub(http);
    }

    private sealed class FilterContextStub(HttpContext httpContext) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext => httpContext;
        public override IList<object?> Arguments => Array.Empty<object?>();
        public override T GetArgument<T>(int index) => throw new NotSupportedException();
    }

    private sealed class RecordingHandle : IAsyncTenantScopeHandle
    {
        public int CommitCount { get; private set; }

        public Task CommitAsync(CancellationToken ct)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingHandle(Exception throwOnCommit) : IAsyncTenantScopeHandle
    {
        public Task CommitAsync(CancellationToken ct) => Task.FromException(throwOnCommit);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
