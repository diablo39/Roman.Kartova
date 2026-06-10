using Kartova.Organization.Application;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

/// <summary>
/// Shape tests for <see cref="ChangeMemberRoleResult"/>. One test per terminal
/// branch — asserts every field so mutation testing cannot flip a boolean literal
/// without a guaranteed failing observer.
/// </summary>
[TestClass]
public sealed class ChangeMemberRoleResultTests
{
    [TestMethod]
    public void Success_sets_only_Changed()
    {
        var r = ChangeMemberRoleResult.Success;

        Assert.IsTrue(r.Changed);
        Assert.IsFalse(r.NotFound);
        Assert.IsFalse(r.InvalidRole);
        Assert.IsFalse(r.LastOrgAdmin);
    }

    [TestMethod]
    public void NotFoundResult_sets_only_NotFound()
    {
        var r = ChangeMemberRoleResult.NotFoundResult;

        Assert.IsFalse(r.Changed);
        Assert.IsTrue(r.NotFound);
        Assert.IsFalse(r.InvalidRole);
        Assert.IsFalse(r.LastOrgAdmin);
    }

    [TestMethod]
    public void InvalidRoleResult_sets_only_InvalidRole()
    {
        var r = ChangeMemberRoleResult.InvalidRoleResult;

        Assert.IsFalse(r.Changed);
        Assert.IsFalse(r.NotFound);
        Assert.IsTrue(r.InvalidRole);
        Assert.IsFalse(r.LastOrgAdmin);
    }

    [TestMethod]
    public void LastOrgAdminResult_sets_only_LastOrgAdmin()
    {
        var r = ChangeMemberRoleResult.LastOrgAdminResult;

        Assert.IsFalse(r.Changed);
        Assert.IsFalse(r.NotFound);
        Assert.IsFalse(r.InvalidRole);
        Assert.IsTrue(r.LastOrgAdmin);
    }
}
