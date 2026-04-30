using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

/// <summary>
/// Pins the three branches of <see cref="TenantScopeCommitEndpointFilter"/>: happy
/// commit, missing-handle programmer-error, and commit-throws bubbling. The integration
/// suite covers the happy path implicitly through every authenticated request, but
/// the failure branches were uncovered (the slice-3 coverage run flagged the filter
/// at 60% line coverage).
/// </summary>
public class TenantScopeCommitEndpointFilterTests
{
    [Fact]
    public async Task Invoke_commits_active_handle_then_returns_inner_result()
    {
        var handle = new RecordingHandle();
        var ctx = MakeContext(handle);
        var sut = new TenantScopeCommitEndpointFilter();

        var inner = Results.Ok("payload");
        var result = await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(inner));

        result.Should().BeSameAs(inner, because: "the IResult is returned to ASP.NET so it can ExecuteAsync " +
                                                  "AFTER commit succeeds — preserving ADR-0090 durability");
        handle.CommitCount.Should().Be(1, because: "exactly one commit per scoped request");
    }

    [Fact]
    public async Task Invoke_throws_InvalidOperationException_when_handle_is_missing()
    {
        // No HandleKey on HttpContext.Items → indicates begin-middleware was not wired
        // before endpoint dispatch. Filter must surface this loudly rather than silently
        // skipping commit and leaving the transaction dangling.
        var ctx = MakeContext(handle: null);
        var sut = new TenantScopeCommitEndpointFilter();

        var act = () => sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(Results.Ok())).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*TenantScopeCommitEndpointFilter ran without an active scope handle*")
            .WithMessage("*TenantScopeBeginMiddleware*");
    }

    [Fact]
    public async Task Invoke_lets_commit_exception_bubble_to_UseExceptionHandler()
    {
        // ADR-0090 requires that a commit failure produce a 500 with no partial body —
        // the filter does this by simply not catching the exception, so UseExceptionHandler
        // and AddProblemDetails (ADR-0091) can map it to RFC 7807.
        var handle = new ThrowingHandle(new InvalidOperationException("connection lost"));
        var ctx = MakeContext(handle);
        var sut = new TenantScopeCommitEndpointFilter();

        var act = () => sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(Results.Ok())).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("connection lost");
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
