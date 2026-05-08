using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Wolverine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wolverine;

namespace Kartova.SharedKernel.Tests;

/// <summary>
/// Coverage for the Wolverine async-handler tenant-scope middleware. The middleware
/// is dormant in the codebase today (ADR-0093 keeps Wolverine to outbox/Kafka while
/// HTTP handlers direct-dispatch) but it is the canonical integration point for the
/// first async tenant-scoped handler, so we pin its three branches now rather than
/// let it sit at 0% coverage waiting for a slice that may not arrive for months.
/// </summary>
[TestClass]
public class TenantScopeWolverineMiddlewareTests
{
    [TestMethod]
    public async Task BeforeAsync_returns_null_when_envelope_has_no_tenant_header()
    {
        var envelope = new Envelope { Headers = { } };
        var ctx = new TenantContextAccessor();
        var scope = new RecordingTenantScope();

        var handle = await TenantScopeWolverineMiddleware.BeforeAsync(envelope, ctx, scope, CancellationToken.None);

        // Messages without tenant_id are platform-admin / system messages and must not enter a tenant scope.
        Assert.IsNull(handle);
        // scope.BeginAsync should be skipped on the no-header path.
        Assert.IsNull(scope.BeganFor);
        Assert.IsFalse(ctx.IsTenantScoped);
    }

    [TestMethod]
    public async Task BeforeAsync_returns_null_when_tenant_header_is_unparseable()
    {
        var envelope = new Envelope { Headers = { [KartovaClaims.TenantId] = "not-a-guid" } };
        var ctx = new TenantContextAccessor();
        var scope = new RecordingTenantScope();

        var handle = await TenantScopeWolverineMiddleware.BeforeAsync(envelope, ctx, scope, CancellationToken.None);

        // An unparseable tenant_id is treated as absent — the alternative
        // (throwing here) would be silently retried by Wolverine until DLQ.
        Assert.IsNull(handle);
    }

    [TestMethod]
    public async Task BeforeAsync_populates_context_and_begins_scope_when_tenant_header_is_a_valid_guid()
    {
        var tenantId = Guid.NewGuid();
        var envelope = new Envelope { Headers = { [KartovaClaims.TenantId] = tenantId.ToString() } };
        var ctx = new TenantContextAccessor();
        var fakeHandle = new FakeHandle();
        var scope = new RecordingTenantScope { ReturnHandle = fakeHandle };

        var handle = await TenantScopeWolverineMiddleware.BeforeAsync(envelope, ctx, scope, CancellationToken.None);

        Assert.AreSame(fakeHandle, handle);
        Assert.AreEqual(new TenantId(tenantId), scope.BeganFor);
        Assert.AreEqual(new TenantId(tenantId), ctx.Id);
    }

    [TestMethod]
    public async Task AfterAsync_commits_when_handle_is_present()
    {
        var handle = new FakeHandle();

        await TenantScopeWolverineMiddleware.AfterAsync(handle, CancellationToken.None);

        Assert.AreEqual(1, handle.CommitCount);
    }

    [TestMethod]
    public async Task AfterAsync_is_a_noop_when_handle_is_null()
    {
        // No throw — the BeforeAsync no-tenant path returns null and AfterAsync must tolerate it.
        await TenantScopeWolverineMiddleware.AfterAsync(null, CancellationToken.None);
    }

    [TestMethod]
    public async Task FinallyAsync_disposes_when_handle_is_present()
    {
        var handle = new FakeHandle();

        await TenantScopeWolverineMiddleware.FinallyAsync(handle);

        Assert.AreEqual(1, handle.DisposeCount);
    }

    [TestMethod]
    public async Task FinallyAsync_is_a_noop_when_handle_is_null()
    {
        await TenantScopeWolverineMiddleware.FinallyAsync(null);
    }

    private sealed class RecordingTenantScope : ITenantScope
    {
        public TenantId? BeganFor { get; private set; }
        public IAsyncTenantScopeHandle? ReturnHandle { get; init; }

        public bool IsActive => BeganFor is not null;

        public Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct)
        {
            BeganFor = id;
            return Task.FromResult<IAsyncTenantScopeHandle>(ReturnHandle ?? new FakeHandle());
        }
    }

    private sealed class FakeHandle : IAsyncTenantScopeHandle
    {
        public int CommitCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Task CommitAsync(CancellationToken ct)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
