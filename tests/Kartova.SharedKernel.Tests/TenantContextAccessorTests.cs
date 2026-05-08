using Kartova.SharedKernel.Multitenancy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests;

[TestClass]
public class TenantContextAccessorTests
{
    private static TenantId NewTenantId() => new(Guid.NewGuid());

    // ---- MC/DC for IsTenantScoped = _populated && _id != TenantId.Empty ----
    // Atomic A = _populated, B = _id != TenantId.Empty.
    // Unique-Cause MC/DC pairs:
    //   T1 (A=T, B=T) -> true    — baseline
    //   T2 (A=F, B=T) -> false   — UNREACHABLE via public API: _id is only set when
    //                              _populated becomes true (both mutate inside Populate),
    //                              so we cannot observe A=false with B=true without
    //                              reflection. Documented here and intentionally omitted.
    //   T3 (A=T, B=F) -> false   — Populate(TenantId.Empty, ...) flips A to T but keeps B=F.
    // Case (a) below (fresh instance) gives A=F with B=F — not an MC/DC pair partner by itself,
    // but still exercises the short-circuit false branch and is included for defensive coverage.

    [TestMethod]
    public void IsTenantScoped_is_false_on_fresh_instance_covers_A_false_branch()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        var isScoped = sut.IsTenantScoped;

        // Assert
        Assert.IsFalse(isScoped);
        Assert.AreEqual(TenantId.Empty, sut.Id);
        Assert.IsNotNull(sut.Roles);
        Assert.AreEqual(0, sut.Roles.Count());
    }

    [TestMethod]
    public void IsTenantScoped_is_true_when_populated_with_non_empty_tenant_id_MC_DC_T1()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var id = NewTenantId();

        // Act
        sut.Populate(id, new[] { "OrgAdmin" });

        // Assert — MC/DC pair partner: A=T, B=T -> true
        Assert.IsTrue(sut.IsTenantScoped);
    }

    [TestMethod]
    public void IsTenantScoped_is_false_when_populated_with_empty_tenant_id_MC_DC_T3()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        sut.Populate(TenantId.Empty, new[] { "Member" });

        // Assert — MC/DC pair partner: A=T, B=F -> false.
        // Flipping B from T (T1) to F while holding A=T flips the decision, proving B
        // independently affects the outcome.
        Assert.IsFalse(sut.IsTenantScoped);
    }

    // ---- Id accessor ----

    [TestMethod]
    public void Id_returns_value_supplied_to_Populate()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var id = new TenantId(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        // Act
        sut.Populate(id, Array.Empty<string>());

        // Assert
        Assert.AreEqual(id, sut.Id);
        Assert.AreEqual(Guid.Parse("11111111-1111-1111-1111-111111111111"), sut.Id.Value);
    }

    [TestMethod]
    public void Id_resets_to_Empty_after_Clear()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        sut.Populate(NewTenantId(), new[] { "r" });

        // Act
        sut.Clear();

        // Assert
        Assert.AreEqual(TenantId.Empty, sut.Id);
        Assert.AreEqual(Guid.Empty, sut.Id.Value);
    }

    // ---- Roles accessor ----

    [TestMethod]
    public void Roles_returns_exact_collection_supplied_to_Populate()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var roles = new[] { "OrgAdmin", "Member", "Viewer" };

        // Act
        sut.Populate(NewTenantId(), roles);

        // Assert — exact count AND exact contents (order-sensitive: test name says "exact")
        Assert.AreEqual(3, sut.Roles.Count());
        CollectionAssert.AreEqual(new[] { "OrgAdmin", "Member", "Viewer" }, sut.Roles.ToArray());
    }

    [TestMethod]
    public void Roles_on_fresh_instance_is_empty_but_not_null()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        var roles = sut.Roles;

        // Assert
        Assert.IsNotNull(roles);
        Assert.AreEqual(0, roles.Count());
    }

    [TestMethod]
    public void Populate_with_empty_roles_yields_empty_non_null_collection()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        sut.Populate(NewTenantId(), Array.Empty<string>());

        // Assert
        Assert.IsNotNull(sut.Roles);
        Assert.AreEqual(0, sut.Roles.Count());
    }

    // ---- Null-roles path ----

    [TestMethod]
    public void Populate_with_null_roles_throws_ArgumentNullException()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var id = NewTenantId();

        // Act / Assert — non-nullable parameter contract; null is rejected consistently.
        var ex = Assert.ThrowsExactly<ArgumentNullException>(() => sut.Populate(id, null!));
        Assert.AreEqual("roles", ex.ParamName);
    }

    // ---- Double Populate ----

    [TestMethod]
    public void Second_Populate_replaces_id_and_roles_completely()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var firstId = new TenantId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var secondId = new TenantId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        sut.Populate(firstId, new[] { "r1" });

        // Act
        sut.Populate(secondId, new[] { "r2", "r3" });

        // Assert
        Assert.AreEqual(secondId, sut.Id);
        Assert.AreEqual(2, sut.Roles.Count());
        CollectionAssert.AreEquivalent(new[] { "r2", "r3" }, sut.Roles.ToArray());
        CollectionAssert.DoesNotContain(sut.Roles.ToArray(), "r1");
        Assert.IsTrue(sut.IsTenantScoped);
    }

    // ---- Clear ----

    [TestMethod]
    public void Clear_after_Populate_resets_all_state()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        sut.Populate(NewTenantId(), new[] { "OrgAdmin", "Member" });

        // Act
        sut.Clear();

        // Assert
        Assert.AreEqual(TenantId.Empty, sut.Id);
        Assert.IsNotNull(sut.Roles);
        Assert.AreEqual(0, sut.Roles.Count());
        Assert.IsFalse(sut.IsTenantScoped);
    }

    [TestMethod]
    public void Clear_on_fresh_instance_is_idempotent()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        sut.Clear();

        // Assert
        Assert.AreEqual(TenantId.Empty, sut.Id);
        Assert.AreEqual(0, sut.Roles.Count());
        Assert.IsFalse(sut.IsTenantScoped);
    }

    // ---- Populate -> Clear -> Populate cycle ----

    [TestMethod]
    public void Populate_then_Clear_then_Populate_reflects_second_Populate()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var firstId = new TenantId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var secondId = new TenantId(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        sut.Populate(firstId, new[] { "old-role" });
        sut.Clear();

        // Act
        sut.Populate(secondId, new[] { "new-role-1", "new-role-2" });

        // Assert
        Assert.AreEqual(secondId, sut.Id);
        Assert.AreEqual(2, sut.Roles.Count());
        CollectionAssert.AreEquivalent(new[] { "new-role-1", "new-role-2" }, sut.Roles.ToArray());
        CollectionAssert.DoesNotContain(sut.Roles.ToArray(), "old-role");
        Assert.IsTrue(sut.IsTenantScoped);
    }
}
