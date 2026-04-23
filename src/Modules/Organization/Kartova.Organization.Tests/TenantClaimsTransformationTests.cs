using System.Security.Claims;
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kartova.Organization.Tests;

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

    [Fact]
    public async Task Populates_tenant_id_and_roles_from_JWT_claims()
    {
        var (principal, ctx) = Setup(
            new Claim("tenant_id", "11111111-1111-1111-1111-111111111111"),
            new Claim("realm_access", """{"roles":["OrgAdmin","Member"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        var result = await sut.TransformAsync(principal);

        ctx.IsTenantScoped.Should().BeTrue();
        ctx.Id.Value.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        ctx.Roles.Should().BeEquivalentTo(new[] { "OrgAdmin", "Member" });
        result.IsInRole("OrgAdmin").Should().BeTrue();
    }

    [Fact]
    public async Task Missing_tenant_id_claim_leaves_context_non_tenant_scoped()
    {
        var (principal, ctx) = Setup(
            new Claim("realm_access", """{"roles":["platform-admin"]}""")
        );
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        await sut.TransformAsync(principal);

        ctx.IsTenantScoped.Should().BeFalse();
        ctx.Roles.Should().BeEquivalentTo(new[] { "platform-admin" });
    }

    [Fact]
    public async Task Invalid_tenant_id_claim_leaves_context_non_tenant_scoped()
    {
        var (principal, ctx) = Setup(new Claim("tenant_id", "not-a-guid"));
        var sut = new TenantClaimsTransformation(ProviderFor(ctx));

        await sut.TransformAsync(principal);

        ctx.IsTenantScoped.Should().BeFalse();
    }

    [Fact]
    public async Task Unauthenticated_principal_is_returned_unchanged()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type = not authenticated
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, TenantContextAccessor>();
        var sp = services.BuildServiceProvider();
        var sut = new TenantClaimsTransformation(sp);

        var result = await sut.TransformAsync(principal);

        result.Should().BeSameAs(principal);
        sp.GetRequiredService<ITenantContext>().IsTenantScoped.Should().BeFalse();
    }

    private static IServiceProvider ProviderFor(ITenantContext ctx)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        return services.BuildServiceProvider();
    }
}
