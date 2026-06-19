using Kartova.Audit.Infrastructure;
using Kartova.Catalog.Application;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Pins the ADR-0090 invariant that <see cref="RegisterApplicationHandler"/>
/// reads the tenant id from the ambient <see cref="ITenantContext"/> populated
/// by the transport layer — never from anything in the command payload. The
/// command record has no tenant field, so this test exercises the handler
/// directly while only the scope binds the tenant; any future drift (e.g. a
/// developer adding a "TenantHint" parameter and reading it) will fail here.
/// </summary>
[TestClass]
public sealed class CrossTenantWriteTests : CatalogIntegrationTestBase
{
    [TestMethod]
    public async Task Handler_persists_under_scopes_tenant_regardless_of_payload()
    {
        var orgaTenant = new TenantId(await Fx.GetTenantIdClaimAsync("admin@orga.kartova.local"));
        var orgaUserId = await Fx.GetSubClaimAsync("admin@orga.kartova.local");

        using var hostScope = Fx.Services.CreateScope();
        var sp = hostScope.ServiceProvider;
        var tenantScope = sp.GetRequiredService<ITenantScope>();

        // Populate ITenantContext as the auth/claims-transformation pipeline would.
        var tenantContext = (TenantContextAccessor)sp.GetRequiredService<ITenantContext>();
        tenantContext.Populate(orgaTenant, new[] { KartovaRoles.OrgAdmin });

        await using var handle = await tenantScope.BeginAsync(orgaTenant, default);

        var handler = sp.GetRequiredService<RegisterApplicationHandler>();
        var db = sp.GetRequiredService<CatalogDbContext>();
        var currentUser = new StubCurrentUser(orgaUserId);
        // The handler now writes an audit row. AuditWriter resolves its own
        // ICurrentUser from DI (HttpContext-based), which has no principal in this
        // direct-invocation test — so construct it with the same stub actor the
        // handler uses, mirroring how the HTTP pipeline supplies a real sub claim.
        // The AuditDbContext shares this scope's tenant connection (AddModuleDbContext),
        // so the audit row rides the same transaction as the catalog insert.
        var audit = new AuditWriter(
            sp.GetRequiredService<AuditDbContext>(),
            currentUser,
            tenantContext,
            sp.GetRequiredService<TimeProvider>());

        var resp = await handler.Handle(
            new RegisterApplicationCommand("Scope Wins", "tenant id from scope only", Guid.NewGuid()),
            db,
            tenantContext,
            currentUser,
            audit,
            default);

        await handle.CommitAsync(default);

        Assert.AreEqual(orgaTenant.Value, resp.TenantId);
        Assert.AreEqual(orgaUserId, resp.CreatedByUserId);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid userId) => UserId = userId;
        public Guid UserId { get; }
        public string DisplayName => UserId.ToString();
        public IReadOnlyList<TeamMembershipInfo> TeamMemberships { get; } = Array.Empty<TeamMembershipInfo>();
        public IReadOnlySet<Guid> TeamIds { get; } = new HashSet<Guid>();
    }
}
