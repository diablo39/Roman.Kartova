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
        tenantContext.Populate(tenantId, new[] { KartovaRoles.OrgAdmin });

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
        tenantContext.Populate(tenantId, new[] { KartovaRoles.OrgAdmin });

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
        tenantContext.Populate(tenantId, new[] { KartovaRoles.OrgAdmin });

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
