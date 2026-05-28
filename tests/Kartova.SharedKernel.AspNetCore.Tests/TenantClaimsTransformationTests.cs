using System.Security.Claims;
using System.Text.Json;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public class TenantClaimsTransformationTests
{
    private static (ClaimsPrincipal principal, ITenantContext ctx) Setup(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "test");
        var principal = new ClaimsPrincipal(identity);
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, TenantContextAccessor>();
        var sp = services.BuildServiceProvider();
        return (principal, sp.GetRequiredService<ITenantContext>());
    }

    [TestMethod]
    public async Task Populates_tenant_id_and_roles_from_JWT_claims()
    {
        var (principal, ctx) = Setup(
            new Claim("tenant_id", "11111111-1111-1111-1111-111111111111"),
            new Claim("realm_access", """{"roles":["OrgAdmin","Member"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        var result = await sut.TransformAsync(principal);

        Assert.IsTrue(ctx.IsTenantScoped);
        Assert.AreEqual(Guid.Parse("11111111-1111-1111-1111-111111111111"), ctx.Id.Value);
        // Per BeEquivalentTo audit (2026-05-08): order-independent → AreEquivalent.
        CollectionAssert.AreEquivalent(new[] { "OrgAdmin", "Member" }, ctx.Roles.ToArray());
        Assert.IsTrue(result.IsInRole("OrgAdmin"));
    }

    [TestMethod]
    public async Task Missing_tenant_id_claim_leaves_context_non_tenant_scoped()
    {
        var (principal, ctx) = Setup(
            new Claim("realm_access", """{"roles":["platform-admin"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        await sut.TransformAsync(principal);

        Assert.IsFalse(ctx.IsTenantScoped);
        // Per BeEquivalentTo audit (2026-05-08): order-independent → AreEquivalent.
        CollectionAssert.AreEquivalent(new[] { "platform-admin" }, ctx.Roles.ToArray());
    }

    [TestMethod]
    public async Task Invalid_tenant_id_claim_leaves_context_non_tenant_scoped()
    {
        var (principal, ctx) = Setup(new Claim("tenant_id", "not-a-guid"));
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        await sut.TransformAsync(principal);

        Assert.IsFalse(ctx.IsTenantScoped);
    }

    [TestMethod]
    public async Task Unauthenticated_principal_is_returned_unchanged()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type = not authenticated
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, TenantContextAccessor>();
        var sp = services.BuildServiceProvider();
        var sut = new TenantClaimsTransformation(sp);

        var result = await sut.TransformAsync(principal);

        Assert.AreSame(principal, result);
        Assert.IsFalse(sp.GetRequiredService<ITenantContext>().IsTenantScoped);
    }

    [TestMethod]
    public async Task Expands_role_claims_into_permission_claims_for_Member()
    {
        var (principal, ctx) = Setup(
            new Claim(KartovaClaims.TenantId, "11111111-1111-1111-1111-111111111111"),
            new Claim(KartovaClaims.RealmAccess, """{"roles":["Member"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        var result = await sut.TransformAsync(principal);

        var permClaims = result.FindAll(KartovaClaims.Permission)
                               .Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.Member).ToList(),
            permClaims.ToList());

        Assert.IsFalse(permClaims.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse),
            "Member must not get the reverse-lifecycle permission.");
    }

    [TestMethod]
    public async Task Expands_role_claims_into_permission_claims_for_Viewer()
    {
        var (principal, ctx) = Setup(
            new Claim(KartovaClaims.TenantId, "11111111-1111-1111-1111-111111111111"),
            new Claim(KartovaClaims.RealmAccess, """{"roles":["Viewer"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        var result = await sut.TransformAsync(principal);
        var permClaims = result.FindAll(KartovaClaims.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.Viewer).ToList(),
            permClaims.ToList());
    }

    [TestMethod]
    public async Task Expands_role_claims_into_permission_claims_for_TeamAdmin()
    {
        var (principal, ctx) = Setup(
            new Claim(KartovaClaims.TenantId, "11111111-1111-1111-1111-111111111111"),
            new Claim(KartovaClaims.RealmAccess, """{"roles":["TeamAdmin"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        var result = await sut.TransformAsync(principal);
        var permClaims = result.FindAll(KartovaClaims.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.TeamAdmin).ToList(),
            permClaims.ToList());
    }

    [TestMethod]
    public async Task Expands_role_claims_into_permission_claims_for_OrgAdmin()
    {
        var (principal, ctx) = Setup(
            new Claim(KartovaClaims.TenantId, "11111111-1111-1111-1111-111111111111"),
            new Claim(KartovaClaims.RealmAccess, """{"roles":["OrgAdmin"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        var result = await sut.TransformAsync(principal);
        var permClaims = result.FindAll(KartovaClaims.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin).ToList(),
            permClaims.ToList());
    }

    [TestMethod]
    public async Task Unknown_role_yields_no_permission_claims()
    {
        var (principal, ctx) = Setup(
            new Claim(KartovaClaims.TenantId, "11111111-1111-1111-1111-111111111111"),
            new Claim(KartovaClaims.RealmAccess, """{"roles":["not-a-real-role"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        var result = await sut.TransformAsync(principal);
        Assert.IsFalse(result.HasClaim(c => c.Type == KartovaClaims.Permission),
            "Unknown role must not produce any permission claims.");
    }

    [TestMethod]
    public async Task Duplicate_role_does_not_produce_duplicate_permission_claims()
    {
        var realmAccess = JsonSerializer.Serialize(new { roles = new[] { KartovaRoles.Member, KartovaRoles.Member } });
        var (principal, ctx) = Setup(
            new Claim(KartovaClaims.TenantId, "11111111-1111-1111-1111-111111111111"),
            new Claim(KartovaClaims.RealmAccess, realmAccess));

        var sut = new TenantClaimsTransformation(ProviderFor(ctx));
        var result = await sut.TransformAsync(principal);

        var permClaims = result.FindAll(KartovaClaims.Permission).Select(c => c.Value).ToList();
        var distinctCount = permClaims.Distinct(StringComparer.Ordinal).Count();

        Assert.AreEqual(distinctCount, permClaims.Count,
            "Duplicate role in realm_access must not produce duplicate permission claims.");
        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.Member).ToList(),
            permClaims);
    }

    [TestMethod]
    public async Task Does_not_resolve_post_auth_sync_hooks()
    {
        // Regression test: IClaimsTransformation runs INSIDE UseAuthentication,
        // BEFORE TenantScopeBeginMiddleware opens the per-request connection.
        // Resolving any IPostAuthSyncHook here would (transitively) materialize
        // module DbContexts whose options factory calls ITenantScope.Connection
        // and throws "TenantScope is not active". The hook fan-out lives in
        // TenantScopeBeginMiddleware instead — this test pins that boundary.
        var (principal, ctx) = Setup(
            new Claim(KartovaClaims.TenantId, "11111111-1111-1111-1111-111111111111"),
            new Claim(KartovaClaims.RealmAccess, """{"roles":["Member"]}""")
        );
        var probe = new ThrowingHookProbe();
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddSingleton<IPostAuthSyncHook>(probe);
        var sp = services.BuildServiceProvider();
        var sut = new TenantClaimsTransformation(sp);

        // If the transformation tried to resolve and invoke the hook the probe
        // would throw, failing the test. A successful call proves the fan-out
        // has been moved out of this transformation.
        await sut.TransformAsync(principal);

        Assert.AreEqual(0, probe.ExecuteInvocations,
            "TenantClaimsTransformation must NOT invoke IPostAuthSyncHook implementations — that fan-out belongs to TenantScopeBeginMiddleware.");
    }

    private static IServiceProvider ProviderFor(ITenantContext ctx)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        return services.BuildServiceProvider();
    }

    private sealed class ThrowingHookProbe : IPostAuthSyncHook
    {
        public int ExecuteInvocations { get; private set; }

        public Task ExecuteAsync(ClaimsPrincipal principal, CancellationToken ct)
        {
            ExecuteInvocations++;
            // Simulates the production failure mode: any DbContext-using hook would
            // throw at materialization because TenantScope is not yet active when
            // IClaimsTransformation runs.
            throw new InvalidOperationException(
                "Hook must not be invoked from TenantClaimsTransformation — scope is not active here.");
        }
    }
}
