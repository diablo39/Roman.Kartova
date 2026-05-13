using System.Security.Claims;
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

    private static IServiceProvider ProviderFor(ITenantContext ctx)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        return services.BuildServiceProvider();
    }
}
