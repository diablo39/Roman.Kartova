using FluentAssertions;
using Kartova.Catalog.Application;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Pins the ADR-0090 invariant that <see cref="RegisterApplicationHandler"/>
/// reads the tenant id from the ambient <see cref="ITenantContext"/> populated
/// by the transport layer — never from anything in the command payload. The
/// command record has no tenant field, so this test exercises the handler
/// directly while only the scope binds the tenant; any future drift (e.g. a
/// developer adding a "TenantHint" parameter and reading it) will fail here.
/// </summary>
[Collection(KartovaApiCollection.Name)]
public sealed class CrossTenantWriteTests
{
    private readonly KartovaApiFixture _fx;

    public CrossTenantWriteTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Handler_persists_under_scopes_tenant_regardless_of_payload()
    {
        var orgaTenant = new TenantId(await _fx.GetTenantIdClaimAsync("admin@orga.kartova.local"));
        var orgaUserId = await _fx.GetSubClaimAsync("admin@orga.kartova.local");

        using var hostScope = _fx.Services.CreateScope();
        var sp = hostScope.ServiceProvider;
        var tenantScope = sp.GetRequiredService<ITenantScope>();

        // Populate ITenantContext as the auth/claims-transformation pipeline would.
        var tenantContext = (TenantContextAccessor)sp.GetRequiredService<ITenantContext>();
        tenantContext.Populate(orgaTenant, new[] { "OrgAdmin" });

        await using var handle = await tenantScope.BeginAsync(orgaTenant, default);

        var handler = sp.GetRequiredService<RegisterApplicationHandler>();
        var db = sp.GetRequiredService<CatalogDbContext>();
        var currentUser = new StubCurrentUser(orgaUserId);

        var resp = await handler.Handle(
            new RegisterApplicationCommand("scope-wins", "Scope Wins", "tenant id from scope only"),
            db,
            tenantContext,
            currentUser,
            default);

        await handle.CommitAsync(default);

        resp.TenantId.Should().Be(orgaTenant.Value,
            because: "the handler must source tenant id from the ambient scope, not the payload");
        resp.OwnerUserId.Should().Be(orgaUserId);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid userId) => UserId = userId;
        public Guid UserId { get; }
    }
}
