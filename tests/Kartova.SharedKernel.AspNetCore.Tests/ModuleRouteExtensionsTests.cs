using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public class ModuleRouteExtensionsTests
{
    [TestMethod]
    public async Task MapTenantScopedModule_groups_routes_under_api_v1_slug()
    {
        using var host = await CreateHostAsync(app =>
        {
            var group = app.MapTenantScopedModule("catalog");
            group.MapGet("/applications", () => Results.Ok("ok")).AllowAnonymous();
        });
        var client = host.GetTestClient();
        var resp = await client.GetAsync("/api/v1/catalog/applications");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task MapAdminModule_groups_routes_under_api_v1_admin_slug()
    {
        using var host = await CreateHostAsync(app =>
        {
            var group = app.MapAdminModule("catalog");
            group.MapGet("/applications", () => Results.Ok("ok")).AllowAnonymous();
        });
        var client = host.GetTestClient();
        var resp = await client.GetAsync("/api/v1/admin/catalog/applications");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task MapTenantScopedModule_skip_rule_when_slug_is_plural_collection()
    {
        // Convention: slug IS the URL segment. The "skip" rule is mechanical —
        // the module declares Slug = "organizations" so the URL reads naturally.
        using var host = await CreateHostAsync(app =>
        {
            var group = app.MapTenantScopedModule("organizations");
            group.MapGet("/me", () => Results.Ok("ok")).AllowAnonymous();
        });
        var client = host.GetTestClient();
        var resp = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }

    private static async Task<IHost> CreateHostAsync(Action<WebApplication> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        // Authorization services + middleware must be present because RequireTenantScope /
        // MapAdminModule attach authorization metadata. Inner endpoints use .AllowAnonymous()
        // so the policy doesn't actually run — real auth is exercised at the integration layer.
        builder.Services.AddAuthorization();
        // RequireTenantScope wires TenantScopeBeginMiddleware + the commit endpoint filter,
        // which need ITenantContext / ITenantScope in DI. Stub both so the unit test can
        // assert URL routing without dragging in EF / Postgres.
        builder.Services.AddSingleton<ITenantContext, FakeTenantContext>();
        builder.Services.AddSingleton<ITenantScope, FakeTenantScope>();
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthorization();
        app.UseMiddleware<TenantScopeBeginMiddleware>();
        configure(app);
        await app.StartAsync();
        return app;
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public TenantId Id { get; private set; } = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        public bool IsTenantScoped => true;
        public IReadOnlyCollection<string> Roles { get; private set; } = Array.Empty<string>();
        public void Populate(TenantId id, IReadOnlyCollection<string> roles) { Id = id; Roles = roles; }
        public void Clear() { Id = TenantId.Empty; Roles = Array.Empty<string>(); }
    }

    private sealed class FakeTenantScope : ITenantScope
    {
        public bool IsActive => false;
        public Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct) =>
            Task.FromResult<IAsyncTenantScopeHandle>(new FakeHandle());

        private sealed class FakeHandle : IAsyncTenantScopeHandle
        {
            public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
