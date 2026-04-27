using FluentAssertions;
using Kartova.SharedKernel.Multitenancy;
using Xunit;

namespace Kartova.SharedKernel.Tests;

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

    [Fact]
    public void IsTenantScoped_is_false_on_fresh_instance_covers_A_false_branch()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        var isScoped = sut.IsTenantScoped;

        // Assert
        isScoped.Should().BeFalse();
        sut.Id.Should().Be(TenantId.Empty);
        sut.Roles.Should().NotBeNull();
        sut.Roles.Should().BeEmpty();
    }

    [Fact]
    public void IsTenantScoped_is_true_when_populated_with_non_empty_tenant_id_MC_DC_T1()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var id = NewTenantId();

        // Act
        sut.Populate(id, new[] { "OrgAdmin" });

        // Assert — MC/DC pair partner: A=T, B=T -> true
        sut.IsTenantScoped.Should().BeTrue();
    }

    [Fact]
    public void IsTenantScoped_is_false_when_populated_with_empty_tenant_id_MC_DC_T3()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        sut.Populate(TenantId.Empty, new[] { "Member" });

        // Assert — MC/DC pair partner: A=T, B=F -> false.
        // Flipping B from T (T1) to F while holding A=T flips the decision, proving B
        // independently affects the outcome.
        sut.IsTenantScoped.Should().BeFalse();
    }

    // ---- Id accessor ----

    [Fact]
    public void Id_returns_value_supplied_to_Populate()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var id = new TenantId(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        // Act
        sut.Populate(id, Array.Empty<string>());

        // Assert
        sut.Id.Should().Be(id);
        sut.Id.Value.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    [Fact]
    public void Id_resets_to_Empty_after_Clear()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        sut.Populate(NewTenantId(), new[] { "r" });

        // Act
        sut.Clear();

        // Assert
        sut.Id.Should().Be(TenantId.Empty);
        sut.Id.Value.Should().Be(Guid.Empty);
    }

    // ---- Roles accessor ----

    [Fact]
    public void Roles_returns_exact_collection_supplied_to_Populate()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var roles = new[] { "OrgAdmin", "Member", "Viewer" };

        // Act
        sut.Populate(NewTenantId(), roles);

        // Assert — exact count AND exact contents
        sut.Roles.Should().HaveCount(3);
        sut.Roles.Should().BeEquivalentTo(new[] { "OrgAdmin", "Member", "Viewer" });
    }

    [Fact]
    public void Roles_on_fresh_instance_is_empty_but_not_null()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        var roles = sut.Roles;

        // Assert
        roles.Should().NotBeNull();
        roles.Should().BeEmpty();
        roles.Should().HaveCount(0);
    }

    [Fact]
    public void Populate_with_empty_roles_yields_empty_non_null_collection()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        sut.Populate(NewTenantId(), Array.Empty<string>());

        // Assert
        sut.Roles.Should().NotBeNull();
        sut.Roles.Should().BeEmpty();
    }

    // ---- Null-roles path ----

    [Fact]
    public void Populate_with_null_roles_throws_ArgumentNullException()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        var id = NewTenantId();

        // Act
        var act = () => sut.Populate(id, null!);

        // Assert — non-nullable parameter contract; null is rejected consistently.
        act.Should().Throw<ArgumentNullException>().WithParameterName("roles");
    }

    // ---- Double Populate ----

    [Fact]
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
        sut.Id.Should().Be(secondId);
        sut.Roles.Should().HaveCount(2);
        sut.Roles.Should().BeEquivalentTo(new[] { "r2", "r3" });
        sut.Roles.Should().NotContain("r1");
        sut.IsTenantScoped.Should().BeTrue();
    }

    // ---- Clear ----

    [Fact]
    public void Clear_after_Populate_resets_all_state()
    {
        // Arrange
        var sut = new TenantContextAccessor();
        sut.Populate(NewTenantId(), new[] { "OrgAdmin", "Member" });

        // Act
        sut.Clear();

        // Assert
        sut.Id.Should().Be(TenantId.Empty);
        sut.Roles.Should().NotBeNull();
        sut.Roles.Should().BeEmpty();
        sut.IsTenantScoped.Should().BeFalse();
    }

    [Fact]
    public void Clear_on_fresh_instance_is_idempotent()
    {
        // Arrange
        var sut = new TenantContextAccessor();

        // Act
        sut.Clear();

        // Assert
        sut.Id.Should().Be(TenantId.Empty);
        sut.Roles.Should().BeEmpty();
        sut.IsTenantScoped.Should().BeFalse();
    }

    // ---- Populate -> Clear -> Populate cycle ----

    [Fact]
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
        sut.Id.Should().Be(secondId);
        sut.Roles.Should().HaveCount(2);
        sut.Roles.Should().BeEquivalentTo(new[] { "new-role-1", "new-role-2" });
        sut.Roles.Should().NotContain("old-role");
        sut.IsTenantScoped.Should().BeTrue();
    }
}
