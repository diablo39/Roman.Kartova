using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

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
        var app = DomainApplication.Create("payments-api", "Payments API", "Payments REST surface.", Owner, Tenant, Clock());

        Assert.AreEqual("payments-api", app.Name);
        Assert.AreEqual("Payments API", app.DisplayName);
        Assert.AreEqual("Payments REST surface.", app.Description);
        Assert.AreEqual(Owner, app.OwnerUserId);
        Assert.AreEqual(Tenant, app.TenantId);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t\n")]
    public void Create_throws_on_empty_or_whitespace_name(string name)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "name");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t")]
    public void Create_throws_ArgumentException_with_empty_message_for_blank_name(string emptyName)
    {
        // Kills mutant at line 87: `throw new ArgumentException("Application name must not be empty.", ...)` mutated to `;`.
        // With the throw removed, empty/whitespace names fall through to the kebab-case check which throws a
        // DIFFERENT message ("kebab-case"). Asserting on "empty" in the message pins the specific guard.
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create(emptyName, "Display Name", "desc", Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "empty");
    }

    [TestMethod]
    public void Create_throws_on_name_over_256_chars()
    {
        var name = new string('x', 257);
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "256");
    }

    [TestMethod]
    [DataRow("BadName")]            // uppercase
    [DataRow("bad name")]           // space
    [DataRow("bad_name")]           // underscore
    [DataRow("bad--name")]          // double dash
    [DataRow("-leading")]           // leading dash
    [DataRow("trailing-")]          // trailing dash
    [DataRow("9digit")]             // leading digit
    [DataRow("Mixed-Case")]         // mixed case
    [DataRow("kebab.with.dot")]     // dot
    public void Create_throws_on_non_kebab_case_name(string name)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "kebab-case");
    }

    [TestMethod]
    [DataRow("a")]                  // single letter
    [DataRow("abc")]                // single segment
    [DataRow("payment-gateway")]    // canonical form
    [DataRow("a1")]                 // letter + digit
    [DataRow("a-b-c-d")]            // many segments
    [DataRow("v2-api")]             // segment with digit
    public void Create_succeeds_with_kebab_case_name(string name)
    {
        var app = DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock());
        Assert.AreEqual(name, app.Name);
    }

    [TestMethod]
    public void Create_succeeds_with_name_at_exactly_256_chars()
    {
        // Boundary pin — the invariant is `length > 256 throws`, so 256 must succeed.
        // Without this test the off-by-one mutation `length >= 256` survives.
        var name = new string('x', 256);
        var app = DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock());
        Assert.AreEqual(256, app.Name.Length);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_or_whitespace_displayName(string displayName)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create("name", displayName, "desc", Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "display name");
    }

    [TestMethod]
    public void Create_throws_on_displayName_over_128_chars()
    {
        var displayName = new string('x', 129);
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create("name", displayName, "desc", Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "128");
    }

    [TestMethod]
    public void Create_succeeds_on_displayName_at_128_chars()
    {
        var displayName = new string('x', 128);
        var app = DomainApplication.Create("name", displayName, "desc", Owner, Tenant, Clock());
        Assert.AreEqual(displayName, app.DisplayName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_or_whitespace_description(string description)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create("name", "Display Name", description, Owner, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "description");
    }

    [TestMethod]
    public void Create_throws_on_empty_owner_user_id()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DomainApplication.Create("name", "Display Name", "desc", Guid.Empty, Tenant, Clock()));
        StringAssert.Contains(ex.Message, "ownerUserId");
    }

    [TestMethod]
    public void Create_assigns_fresh_id_each_call()
    {
        var a = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, Clock());
        var b = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, Clock());
        Assert.AreNotEqual(b.Id, a.Id);
        Assert.AreNotEqual(Guid.Empty, a.Id.Value);
    }

    [TestMethod]
    public void Create_uses_clock_GetUtcNow_for_CreatedAt()
    {
        var clock = Clock();

        var app = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, clock);

        Assert.AreEqual(clock.GetUtcNow(), app.CreatedAt);
        Assert.AreEqual(TimeSpan.Zero, app.CreatedAt.Offset);
    }

    [TestMethod]
    public void Create_with_null_clock_throws()
    {
        var ex = Assert.ThrowsExactly<ArgumentNullException>(
            () => DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, clock: null!));
        Assert.AreEqual("clock", ex.ParamName);
    }
}
