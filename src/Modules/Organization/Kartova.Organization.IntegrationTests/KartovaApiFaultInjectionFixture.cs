using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Subclass of <see cref="KartovaApiFixture"/> that:
/// <list type="bullet">
///   <item>Injects a <see cref="FailingCommitTenantScopeDecorator"/> wrapping the real
///         <see cref="ITenantScope"/>. Tests flip <see cref="CommitFailFlag.Fail"/> to
///         force <c>CommitAsync</c> to throw.</item>
///   <item>Maps a tenant-scoped streaming endpoint <c>/__test/stream</c> via
///         <see cref="IStartupFilter"/> so <see cref="StreamingDurabilityTests"/> can
///         exercise the durability promise of ADR-0090.</item>
/// </list>
/// Test-only.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class KartovaApiFaultInjectionFixture : KartovaApiFixture
{
    public CommitFailFlag CommitFailFlag { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(CommitFailFlag);

            // Replace the ITenantScope registration with the decorator. The decorator
            // resolves the concrete TenantScope from DI directly (NOT via
            // GetRequiredService<ITenantScope>(), which would create a cycle), then
            // wraps it. RemoveAll<ITenantScope>() leaves the concrete TenantScope
            // registration in place — see ADR-0090 / DI shape note in the plan.
            services.RemoveAll<ITenantScope>();
            services.AddScoped<ITenantScope>(sp =>
                new FailingCommitTenantScopeDecorator(
                    sp.GetRequiredService<Kartova.SharedKernel.Postgres.TenantScope>(),
                    sp.GetRequiredService<CommitFailFlag>()));

            services.AddSingleton<IStartupFilter, StreamingTestEndpointStartupFilter>();
        });
    }
}

[ExcludeFromCodeCoverage]
public sealed class CommitFailFlag
{
    public bool Fail { get; set; }
}

[ExcludeFromCodeCoverage]
internal sealed class FailingCommitTenantScopeDecorator : ITenantScope
{
    private readonly ITenantScope _inner;
    private readonly CommitFailFlag _flag;

    public FailingCommitTenantScopeDecorator(ITenantScope inner, CommitFailFlag flag)
    {
        _inner = inner;
        _flag = flag;
    }

    public bool IsActive => _inner.IsActive;

    public async Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct)
    {
        var inner = await _inner.BeginAsync(id, ct);
        return new Handle(inner, _flag);
    }

    private sealed class Handle : IAsyncTenantScopeHandle
    {
        private readonly IAsyncTenantScopeHandle _inner;
        private readonly CommitFailFlag _flag;

        public Handle(IAsyncTenantScopeHandle inner, CommitFailFlag flag)
        {
            _inner = inner;
            _flag = flag;
        }

        public Task CommitAsync(CancellationToken ct)
        {
            if (_flag.Fail)
            {
                throw new InvalidOperationException(
                    "Forced commit failure for streaming-durability regression test.");
            }
            return _inner.CommitAsync(ct);
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}

[ExcludeFromCodeCoverage]
internal sealed class StreamingTestEndpointStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            next(app);
            // Map a tenant-scoped streaming endpoint AFTER the main pipeline so
            // RequireTenantScope() wires the marker metadata + commit filter
            // identically to production endpoints.
            app.UseEndpoints(endpoints =>
            {
                var group = endpoints.MapGroup("/__test").RequireTenantScope();
                group.MapGet("/stream", () =>
                {
                    // Returns a streaming IResult whose ExecuteAsync writes 2 KB to
                    // Response.Body. The commit filter awaits CommitAsync BEFORE the
                    // result executes, so a forced commit failure must surface as
                    // 5xx + problem-details with no body bytes flushed. Writing into
                    // ctx.Response.Body directly inside the handler would defeat the
                    // promise — bytes would flush before the filter awaits commit —
                    // which is precisely why ADR-0090 documents the IResult-returning
                    // contract for streaming endpoints.
                    return Results.Stream(async stream =>
                    {
                        var buffer = new byte[256];
                        for (var i = 0; i < 8; i++)
                        {
                            await stream.WriteAsync(buffer);
                        }
                    }, contentType: "application/octet-stream");
                });
            });
        };
}
