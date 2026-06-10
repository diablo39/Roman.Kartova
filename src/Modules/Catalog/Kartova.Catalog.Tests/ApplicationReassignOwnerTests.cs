using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

// Same aliasing rationale as ApplicationTests: the sibling Kartova.Catalog.Application
// namespace wins simple-name lookup for `Application`, so alias the domain type.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Domain tests for <see cref="DomainApplication.ReassignOwner"/> (slice-10 Task 6).
/// Exercises the cross-module owner-transfer mutator consumed by
/// <c>IApplicationOwnerReassigner</c>.
/// </summary>
[TestClass]
public sealed class ApplicationReassignOwnerTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099"));
    private static readonly Guid OriginalOwner = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset Now = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

    private static DomainApplication NewApp() => DomainApplication.Create(
        "Payments API", "Payments REST surface.", OriginalOwner, Tenant, new FakeTimeProvider(Now));

    [TestMethod]
    public void ReassignOwner_sets_new_owner()
    {
        var app = NewApp();
        var newOwner = Guid.Parse("dddddddd-0000-0000-0000-000000000002");

        app.ReassignOwner(newOwner);

        Assert.AreEqual(newOwner, app.OwnerUserId);
    }

    [TestMethod]
    public void ReassignOwner_rejects_empty_guid()
    {
        var app = NewApp();

        var ex = Assert.ThrowsExactly<ArgumentException>(() => app.ReassignOwner(Guid.Empty));
        StringAssert.Contains(ex.Message, "newOwnerUserId");

        // Owner must be unchanged after the rejected reassignment.
        Assert.AreEqual(OriginalOwner, app.OwnerUserId);
    }
}
