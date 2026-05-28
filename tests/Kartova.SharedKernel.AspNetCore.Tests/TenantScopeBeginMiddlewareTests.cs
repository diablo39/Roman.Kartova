using System.Security.Claims;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

/// <summary>
/// Pins <see cref="TenantScopeBeginMiddleware"/> behaviour around team-membership
/// population (slice 8). Population MUST happen after <c>ITenantScope.BeginAsync</c>
/// so the request-scoped DbContext is bound to a connection where
/// <c>SET LOCAL app.current_tenant_id</c> has executed — otherwise the
/// <c>team_members</c> RLS policy cannot evaluate.
///
/// Missing-reader is a configuration error (the reader is unconditionally
/// registered in production by OrganizationModule), surfaced through
/// <c>GetRequiredService</c>. Missing-or-non-Guid <c>sub</c> claim is the only
/// path that legitimately leaves memberships empty — and even then the middleware
/// emits a warning so the failure is visible in observability.
/// </summary>
[TestClass]
public sealed class TenantScopeBeginMiddlewareTests
{
    [TestMethod]
    public async Task Middleware_populates_team_memberships_after_tenant_scope_begins()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = new TenantId(Guid.NewGuid());
        var memberships = new List<TeamMembershipInfo>
        {
            new(Guid.NewGuid(), TeamRoleKind.Admin),
            new(Guid.NewGuid(), TeamRoleKind.Member),
        };

        var reader = Substitute.For<ITeamMembershipReader>();
        reader.GetForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TeamMembershipInfo>>(memberships));

        var handle = Substitute.For<IAsyncTenantScopeHandle>();
        handle.DisposeAsync().Returns(ValueTask.CompletedTask);

        var scope = Substitute.For<ITenantScope>();
        scope.BeginAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(handle));

        var tenantContext = new TenantContextAccessor();
        tenantContext.Populate(tenantId, new[] { "OrgAdmin" });

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(scope);
        services.AddSingleton(reader);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", userId.ToString()) },
            authenticationType: "test"));

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new RequireTenantScopeMarker()),
            displayName: "test-endpoint");
        httpContext.SetEndpoint(endpoint);

        var sut = new TenantScopeBeginMiddleware(_ => Task.CompletedTask, Substitute.For<ILogger<TenantScopeBeginMiddleware>>());

        // Act
        await sut.InvokeAsync(httpContext);

        // Assert
        Assert.AreEqual(2, tenantContext.TeamMemberships.Count);
        CollectionAssert.AreEquivalent(
            memberships.Select(m => m.TeamId).ToList(),
            tenantContext.TeamIds.ToList());
    }

    [TestMethod]
    public async Task Middleware_disposes_handle_when_membership_reader_throws()
    {
        // Arrange — wire reader to throw; assert the handle's DisposeAsync still fires.
        var userId = Guid.NewGuid();
        var tenantId = new TenantId(Guid.NewGuid());

        var reader = Substitute.For<ITeamMembershipReader>();
        reader.GetForUserAsync(userId, Arg.Any<CancellationToken>())
              .Returns<Task<IReadOnlyList<TeamMembershipInfo>>>(_ => throw new InvalidOperationException("simulated DB failure"));

        var handle = Substitute.For<IAsyncTenantScopeHandle>();
        var scope = Substitute.For<ITenantScope>();
        scope.BeginAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>()).Returns(handle);

        var tenantContext = new TenantContextAccessor();
        tenantContext.Populate(tenantId, new[] { "OrgAdmin" });

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(scope);
        services.AddSingleton(reader);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", userId.ToString()) }, "test"));

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new RequireTenantScopeMarker()),
            displayName: "test-endpoint");
        httpContext.SetEndpoint(endpoint);

        var sut = new TenantScopeBeginMiddleware(_ => Task.CompletedTask, Substitute.For<ILogger<TenantScopeBeginMiddleware>>());

        // Act + Assert: reader throws → middleware propagates, BUT handle.DisposeAsync MUST have run.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sut.InvokeAsync(httpContext));

        await handle.Received(1).DisposeAsync();
    }

    [TestMethod]
    public async Task Middleware_invokes_post_auth_sync_hooks_after_BeginAsync_succeeds()
    {
        // Regression test for slice-9 Phase D: hooks were originally fanned out
        // from TenantClaimsTransformation, which runs INSIDE UseAuthentication
        // — BEFORE this middleware opens the per-request connection. A hook that
        // resolved any AddModuleDbContext-registered DbContext therefore threw
        // "TenantScope is not active" at materialization (the options factory
        // calls scope.Connection). The fix moved the fan-out here, AFTER
        // BeginAsync returns its handle.
        var userId = Guid.NewGuid();
        var tenantId = new TenantId(Guid.NewGuid());

        var reader = Substitute.For<ITeamMembershipReader>();
        reader.GetForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TeamMembershipInfo>>(Array.Empty<TeamMembershipInfo>()));

        var handle = Substitute.For<IAsyncTenantScopeHandle>();
        handle.DisposeAsync().Returns(ValueTask.CompletedTask);

        var beginCalledAt = 0;
        var hookCalledAt = 0;
        var counter = 0;

        var scope = Substitute.For<ITenantScope>();
        scope.BeginAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                beginCalledAt = ++counter;
                return Task.FromResult(handle);
            });

        var spy = new OrderedSpyHook(() => hookCalledAt = ++counter);

        var tenantContext = new TenantContextAccessor();
        tenantContext.Populate(tenantId, new[] { "OrgAdmin" });

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(scope);
        services.AddSingleton(reader);
        services.AddSingleton<IPostAuthSyncHook>(spy);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", userId.ToString()) }, "test"));
        httpContext.User = principal;

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new RequireTenantScopeMarker()),
            displayName: "test-endpoint");
        httpContext.SetEndpoint(endpoint);

        var sut = new TenantScopeBeginMiddleware(_ => Task.CompletedTask, Substitute.For<ILogger<TenantScopeBeginMiddleware>>());

        await sut.InvokeAsync(httpContext);

        Assert.AreEqual(1, spy.Invocations, "Hook must be invoked exactly once per request.");
        Assert.AreSame(principal, spy.CapturedPrincipal,
            "Hook must receive the request's ClaimsPrincipal so it can read sub/email/given_name/family_name.");
        Assert.IsTrue(beginCalledAt > 0 && hookCalledAt > 0, "Both observers must have fired.");
        Assert.IsTrue(beginCalledAt < hookCalledAt,
            "Hook must run AFTER ITenantScope.BeginAsync so any DbContext it resolves sees an active scope.");
        await handle.Received(1).DisposeAsync();
    }

    [TestMethod]
    public async Task Middleware_disposes_handle_when_post_auth_sync_hook_throws()
    {
        // Hook failure must NOT leak the per-request connection — the finally
        // block owns DisposeAsync regardless of where in the try body the throw
        // came from (membership-reader, hook, or downstream pipeline).
        var userId = Guid.NewGuid();
        var tenantId = new TenantId(Guid.NewGuid());

        var reader = Substitute.For<ITeamMembershipReader>();
        reader.GetForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TeamMembershipInfo>>(Array.Empty<TeamMembershipInfo>()));

        var handle = Substitute.For<IAsyncTenantScopeHandle>();
        handle.DisposeAsync().Returns(ValueTask.CompletedTask);

        var scope = Substitute.For<ITenantScope>();
        scope.BeginAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>()).Returns(handle);

        var throwing = new ThrowingHook();

        var tenantContext = new TenantContextAccessor();
        tenantContext.Populate(tenantId, new[] { "OrgAdmin" });

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(scope);
        services.AddSingleton(reader);
        services.AddSingleton<IPostAuthSyncHook>(throwing);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", userId.ToString()) }, "test"));

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new RequireTenantScopeMarker()),
            displayName: "test-endpoint");
        httpContext.SetEndpoint(endpoint);

        var sut = new TenantScopeBeginMiddleware(_ => Task.CompletedTask, Substitute.For<ILogger<TenantScopeBeginMiddleware>>());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sut.InvokeAsync(httpContext));

        await handle.Received(1).DisposeAsync();
    }

    [TestMethod]
    public async Task Middleware_skips_hook_fanout_when_endpoint_lacks_RequireTenantScopeMarker()
    {
        // Endpoints without the marker (anonymous, admin) do not open a scope,
        // and therefore must NOT invoke the hooks — the hooks rely on an active
        // scope to materialize their DbContexts.
        var spy = new OrderedSpyHook(() => { });

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, TenantContextAccessor>();
        services.AddSingleton<IPostAuthSyncHook>(spy);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        // Endpoint with NO RequireTenantScopeMarker.
        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(),
            displayName: "anonymous-endpoint");
        httpContext.SetEndpoint(endpoint);

        var sut = new TenantScopeBeginMiddleware(_ => Task.CompletedTask, Substitute.For<ILogger<TenantScopeBeginMiddleware>>());

        await sut.InvokeAsync(httpContext);

        Assert.AreEqual(0, spy.Invocations,
            "Hooks must not run when the endpoint does not require a tenant scope — there is no active scope for them to use.");
    }

    private sealed class OrderedSpyHook : IPostAuthSyncHook
    {
        private readonly Action _onInvoke;

        public OrderedSpyHook(Action onInvoke) { _onInvoke = onInvoke; }

        public int Invocations { get; private set; }
        public ClaimsPrincipal? CapturedPrincipal { get; private set; }

        public Task ExecuteAsync(ClaimsPrincipal principal, CancellationToken ct)
        {
            Invocations++;
            CapturedPrincipal = principal;
            _onInvoke();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHook : IPostAuthSyncHook
    {
        public Task ExecuteAsync(ClaimsPrincipal principal, CancellationToken ct)
            => throw new InvalidOperationException("simulated hook failure");
    }

    [TestMethod]
    public async Task Middleware_logs_warning_when_sub_claim_missing()
    {
        // Arrange — no 'sub' claim on the principal. Middleware must skip membership
        // population (legitimately: token without a subject identifier carries no
        // membership context) but emit a Warning so the misconfiguration surfaces.
        var tenantId = new TenantId(Guid.NewGuid());

        var reader = Substitute.For<ITeamMembershipReader>();
        var handle = Substitute.For<IAsyncTenantScopeHandle>();
        handle.DisposeAsync().Returns(ValueTask.CompletedTask);

        var scope = Substitute.For<ITenantScope>();
        scope.BeginAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(handle));

        var tenantContext = new TenantContextAccessor();
        tenantContext.Populate(tenantId, new[] { "OrgAdmin" });

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(scope);
        services.AddSingleton(reader);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        // Empty identity — no 'sub' claim.
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "test"));

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new RequireTenantScopeMarker()),
            displayName: "test-endpoint");
        httpContext.SetEndpoint(endpoint);

        var logger = Substitute.For<ILogger<TenantScopeBeginMiddleware>>();
        var sut = new TenantScopeBeginMiddleware(_ => Task.CompletedTask, logger);

        // Act
        await sut.InvokeAsync(httpContext);

        // Assert: reader was NOT called (no sub → skip), memberships remain empty,
        // and the warning fired exactly once.
        await reader.DidNotReceive().GetForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, tenantContext.TeamMemberships.Count);
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
