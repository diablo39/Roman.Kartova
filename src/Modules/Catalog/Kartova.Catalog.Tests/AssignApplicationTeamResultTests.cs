using Kartova.Catalog.Application;
using Kartova.SharedKernel.Multitenancy;

// The `Application` short name is ambiguous between Kartova.Catalog.Domain and the
// sibling Kartova.Catalog.Application namespace; alias the domain type explicitly,
// matching ApplicationTests.cs.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Pin the three terminal factory shapes of <see cref="AssignApplicationTeamResult"/>.
/// Mutation testing flips the boolean literals (false ↔ true) inside the static
/// factories — without per-flag assertions the survivors slip through. Each test
/// asserts every field for one branch, so any single literal flip on
/// <c>(IsSuccess, IsNotFound, IsInvalidTeam, App)</c> fails at least one test.
/// </summary>
[TestClass]
public sealed class AssignApplicationTeamResultTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099"));
    private static readonly Guid Creator = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000099");
    private static readonly Guid Team = Guid.Parse("cccccccc-0000-0000-0000-000000000099");

    [TestMethod]
    public void NotFound_factory_sets_only_IsNotFound()
    {
        var r = AssignApplicationTeamResult.NotFound;

        Assert.IsFalse(r.IsSuccess);
        Assert.IsTrue(r.IsNotFound);
        Assert.IsFalse(r.IsInvalidTeam);
        Assert.IsNull(r.App);
    }

    [TestMethod]
    public void InvalidTeam_factory_sets_only_IsInvalidTeam()
    {
        var r = AssignApplicationTeamResult.InvalidTeam;

        Assert.IsFalse(r.IsSuccess);
        Assert.IsFalse(r.IsNotFound);
        Assert.IsTrue(r.IsInvalidTeam);
        Assert.IsNull(r.App);
    }

    [TestMethod]
    public void Success_factory_sets_IsSuccess_and_App()
    {
        var app = DomainApplication.Create(
            "Display",
            "Description",
            Creator,
            Team,
            Tenant,
            new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));

        var r = AssignApplicationTeamResult.Success(app);

        Assert.IsTrue(r.IsSuccess);
        Assert.IsFalse(r.IsNotFound);
        Assert.IsFalse(r.IsInvalidTeam);
        Assert.AreSame(app, r.App);
    }
}
