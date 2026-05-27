using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

// NOTE: A `using Kartova.Catalog.Domain;` would not bring `Application` into scope
// unambiguously here — the enclosing `Kartova.Catalog` namespace contains a sibling
// child namespace `Kartova.Catalog.Application` which wins simple-name lookup. We
// therefore alias the type explicitly.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ApplicationTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset Now =
        new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null) => TestClocks.At(now ?? Now);

    [TestMethod]
    public void Create_with_valid_args_returns_application()
    {
        var app = DomainApplication.Create("Payments API", "Payments REST surface.", Owner, Tenant, Clock());

        Assert.AreEqual("Payments API", app.DisplayName);
        Assert.AreEqual("Payments REST surface.", app.Description);
        Assert.AreEqual(Owner, app.OwnerUserId);
        Assert.AreEqual(Tenant, app.TenantId);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_or_whitespace_displayName(string displayName)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create(displayName, "desc", Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "display name");
    }

    [TestMethod]
    public void Create_throws_on_displayName_over_128_chars()
    {
        var displayName = new string('x', 129);
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create(displayName, "desc", Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "128");
    }

    [TestMethod]
    public void Create_succeeds_on_displayName_at_128_chars()
    {
        var displayName = new string('x', 128);
        var app = DomainApplication.Create(displayName, "desc", Owner, Tenant, Clock());
        Assert.AreEqual(displayName, app.DisplayName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_or_whitespace_description(string description)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create("Display Name", description, Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "description");
    }

    [TestMethod]
    public void Create_succeeds_on_description_at_4096_chars()
    {
        // SF-3 boundary: domain validator allows exactly 4096 chars. Mirrors the
        // SPA's zod cap so any non-SPA client (CLI, direct API) sees the same
        // ceiling.
        var description = new string('d', 4096);
        var app = DomainApplication.Create("Display Name", description, Owner, Tenant, Clock());
        Assert.AreEqual(4096, app.Description.Length);
    }

    [TestMethod]
    public void Create_throws_on_description_over_4096_chars()
    {
        // SF-3 boundary: 4097 chars must throw. ArgumentException is the contract
        // (ProblemTypes.ValidationFailed at the endpoint layer).
        var description = new string('d', 4097);
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create("Display Name", description, Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "4096");
    }

    [TestMethod]
    public void EditMetadata_throws_on_description_over_4096_chars()
    {
        // Same SF-3 cap applies to the edit path (both call ValidateDescription).
        var app = DomainApplication.Create("Display Name", "ok", Owner, Tenant, Clock());
        var description = new string('d', 4097);
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => app.EditMetadata("Display Name", description));
        StringAssert.Contains(ex.Message, "4096");
    }

    [TestMethod]
    public void Create_throws_on_empty_owner_user_id()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create("Display Name", "desc", Guid.Empty, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "ownerUserId");
    }

    [TestMethod]
    public void Create_assigns_fresh_id_each_call()
    {
        var a = DomainApplication.Create("Display Name", "desc", Owner, Tenant, Clock());
        var b = DomainApplication.Create("Display Name", "desc", Owner, Tenant, Clock());
        Assert.AreNotEqual(b.Id, a.Id);
        Assert.AreNotEqual(Guid.Empty, a.Id.Value);
    }

    [TestMethod]
    public void Create_uses_clock_GetUtcNow_for_CreatedAt()
    {
        var clock = Clock();

        var app = DomainApplication.Create("Display Name", "desc", Owner, Tenant, clock);

        Assert.AreEqual(clock.GetUtcNow(), app.CreatedAt);
        Assert.AreEqual(TimeSpan.Zero, app.CreatedAt.Offset);
    }

    [TestMethod]
    public void Create_with_null_clock_throws()
    {
        var ex = Assert.ThrowsExactly<ArgumentNullException>(
            () => DomainApplication.Create("Display Name", "desc", Owner, Tenant, clock: null!));
        Assert.AreEqual("clock", ex.ParamName);
    }
}
