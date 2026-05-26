using Kartova.Organization.Application;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

/// <summary>
/// Shape tests for the result records produced by Organization command handlers.
/// Mutation testing flips the boolean literals (false ↔ true) inside the handler
/// branches' <c>new(...)</c> constructions, and removes the positional-constructor
/// assignments on the record itself. Asserting every field for every terminal
/// branch — one test per branch — gives each literal flip a guaranteed failing
/// observer.
/// </summary>
[TestClass]
public sealed class HandlerResultTests
{
    // ---------------------------------------------------------------------
    // DeleteTeamResult
    // ---------------------------------------------------------------------

    [TestMethod]
    public void DeleteTeamResult_NotFound_shape()
    {
        var r = new DeleteTeamResult(Deleted: false, NotFound: true, ApplicationsAssigned: null);

        Assert.IsFalse(r.Deleted);
        Assert.IsTrue(r.NotFound);
        Assert.IsNull(r.ApplicationsAssigned);
    }

    [TestMethod]
    public void DeleteTeamResult_HasApplicationsAssigned_shape()
    {
        var r = new DeleteTeamResult(Deleted: false, NotFound: false, ApplicationsAssigned: 3);

        Assert.IsFalse(r.Deleted);
        Assert.IsFalse(r.NotFound);
        Assert.AreEqual(3, r.ApplicationsAssigned);
    }

    [TestMethod]
    public void DeleteTeamResult_Success_shape()
    {
        var r = new DeleteTeamResult(Deleted: true, NotFound: false, ApplicationsAssigned: null);

        Assert.IsTrue(r.Deleted);
        Assert.IsFalse(r.NotFound);
        Assert.IsNull(r.ApplicationsAssigned);
    }

    // ---------------------------------------------------------------------
    // AddTeamMemberResult
    // ---------------------------------------------------------------------

    [TestMethod]
    public void AddTeamMemberResult_TeamNotFound_shape()
    {
        var r = new AddTeamMemberResult(Added: false, TeamNotFound: true, AlreadyMember: false, AddedAt: null);

        Assert.IsFalse(r.Added);
        Assert.IsTrue(r.TeamNotFound);
        Assert.IsFalse(r.AlreadyMember);
        Assert.IsNull(r.AddedAt);
    }

    [TestMethod]
    public void AddTeamMemberResult_AlreadyMember_shape()
    {
        var r = new AddTeamMemberResult(Added: false, TeamNotFound: false, AlreadyMember: true, AddedAt: null);

        Assert.IsFalse(r.Added);
        Assert.IsFalse(r.TeamNotFound);
        Assert.IsTrue(r.AlreadyMember);
        Assert.IsNull(r.AddedAt);
    }

    [TestMethod]
    public void AddTeamMemberResult_Success_shape()
    {
        var when = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);

        var r = new AddTeamMemberResult(Added: true, TeamNotFound: false, AlreadyMember: false, AddedAt: when);

        Assert.IsTrue(r.Added);
        Assert.IsFalse(r.TeamNotFound);
        Assert.IsFalse(r.AlreadyMember);
        Assert.AreEqual(when, r.AddedAt);
    }

    // ---------------------------------------------------------------------
    // RemoveTeamMemberResult
    // ---------------------------------------------------------------------

    [TestMethod]
    public void RemoveTeamMemberResult_TeamNotFound_shape()
    {
        var r = new RemoveTeamMemberResult(Removed: false, TeamNotFound: true, MemberNotFound: false);

        Assert.IsFalse(r.Removed);
        Assert.IsTrue(r.TeamNotFound);
        Assert.IsFalse(r.MemberNotFound);
    }

    [TestMethod]
    public void RemoveTeamMemberResult_MemberNotFound_shape()
    {
        var r = new RemoveTeamMemberResult(Removed: false, TeamNotFound: false, MemberNotFound: true);

        Assert.IsFalse(r.Removed);
        Assert.IsFalse(r.TeamNotFound);
        Assert.IsTrue(r.MemberNotFound);
    }

    [TestMethod]
    public void RemoveTeamMemberResult_Success_shape()
    {
        var r = new RemoveTeamMemberResult(Removed: true, TeamNotFound: false, MemberNotFound: false);

        Assert.IsTrue(r.Removed);
        Assert.IsFalse(r.TeamNotFound);
        Assert.IsFalse(r.MemberNotFound);
    }

    // ---------------------------------------------------------------------
    // UpdateTeamMemberResult
    // ---------------------------------------------------------------------

    [TestMethod]
    public void UpdateTeamMemberResult_TeamNotFound_shape()
    {
        var r = new UpdateTeamMemberResult(Updated: false, TeamNotFound: true, MemberNotFound: false);

        Assert.IsFalse(r.Updated);
        Assert.IsTrue(r.TeamNotFound);
        Assert.IsFalse(r.MemberNotFound);
    }

    [TestMethod]
    public void UpdateTeamMemberResult_MemberNotFound_shape()
    {
        var r = new UpdateTeamMemberResult(Updated: false, TeamNotFound: false, MemberNotFound: true);

        Assert.IsFalse(r.Updated);
        Assert.IsFalse(r.TeamNotFound);
        Assert.IsTrue(r.MemberNotFound);
    }

    [TestMethod]
    public void UpdateTeamMemberResult_Success_shape()
    {
        var r = new UpdateTeamMemberResult(Updated: true, TeamNotFound: false, MemberNotFound: false);

        Assert.IsTrue(r.Updated);
        Assert.IsFalse(r.TeamNotFound);
        Assert.IsFalse(r.MemberNotFound);
    }
}
