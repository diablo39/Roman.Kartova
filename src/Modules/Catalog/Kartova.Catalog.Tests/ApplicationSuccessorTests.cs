using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

// NOTE: A `using Kartova.Catalog.Domain;` would not bring `Application` into scope
// unambiguously here — the enclosing `Kartova.Catalog` namespace contains a sibling
// child namespace `Kartova.Catalog.Application` which wins simple-name lookup. We
// therefore alias the type explicitly.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Application→Application successor reference (ADR-0110, §5.3). Optional
/// lifecycle metadata set on Deprecate, editable via SetSuccessor while
/// Deprecated, and cleared on Reactivate. Self-reference is rejected — the
/// domain has no DB access, so existence validation is a handler concern
/// (C3/C4), not tested here.
/// </summary>
[TestClass]
public sealed class ApplicationSuccessorTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Creator = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Team = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 5, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OtherAppId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    private static FakeTimeProvider Clock(DateTimeOffset? now = null) => TestClocks.At(now ?? Now);

    private static DomainApplication NewActive() =>
        DomainApplication.Create("My App", "Desc.", Creator, Team, Tenant, Clock());

    private static DomainApplication NewDeprecated(Guid? successorApplicationId = null)
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(30), Clock(), successorApplicationId);
        return app;
    }

    [TestMethod]
    public void Deprecate_WithSuccessor_SetsSuccessorApplicationId()
    {
        var app = NewActive();

        app.Deprecate(Now.AddDays(30), Clock(), OtherAppId);

        Assert.AreEqual(OtherAppId, app.SuccessorApplicationId);
    }

    [TestMethod]
    public void Deprecate_WithSelfAsSuccessor_Throws()
    {
        var app = NewActive();

        Assert.ThrowsExactly<ArgumentException>(() => app.Deprecate(Now.AddDays(30), Clock(), app.Id.Value));
    }

    [TestMethod]
    public void SetSuccessor_WhileDeprecated_Updates()
    {
        var app = NewDeprecated();

        app.SetSuccessor(OtherAppId);

        Assert.AreEqual(OtherAppId, app.SuccessorApplicationId);
    }

    [TestMethod]
    public void SetSuccessor_Null_ClearsSuccessor()
    {
        var app = NewDeprecated(successorApplicationId: OtherAppId);

        app.SetSuccessor(null);

        Assert.IsNull(app.SuccessorApplicationId);
    }

    [TestMethod]
    public void SetSuccessor_WhileActive_Throws()
    {
        var app = NewActive();

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(() => app.SetSuccessor(OtherAppId));
        Assert.AreEqual(Lifecycle.Active, ex.CurrentLifecycle);
    }

    [TestMethod]
    public void SetSuccessor_WithSelf_Throws()
    {
        var app = NewDeprecated();

        Assert.ThrowsExactly<ArgumentException>(() => app.SetSuccessor(app.Id.Value));
    }

    [TestMethod]
    public void Reactivate_ClearsSuccessorAndSunset()
    {
        var app = NewDeprecated(successorApplicationId: OtherAppId);

        app.Reactivate();

        Assert.IsNull(app.SuccessorApplicationId);
        Assert.IsNull(app.SunsetDate);
    }
}
