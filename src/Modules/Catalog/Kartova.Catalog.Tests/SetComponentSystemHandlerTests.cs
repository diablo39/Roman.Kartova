using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.AspNetCore;   // ICurrentUser lives here, NOT in Multitenancy
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;   // FakeTimeProvider (via TestClocks)
using NSubstitute;

namespace Kartova.Catalog.Tests;

/// <summary>Handler-level tests for the System membership setter: it must execute
/// <see cref="SystemMembership.Decide"/> against the DbContext and write the matching
/// audit entries. Replace/clear over real SQL is covered by the real-seam integration
/// tests (SetComponentSystemTests) — this suite pins orchestration + audit.
///
/// <para><b>Step-2 contingency, wider than anticipated:</b> the brief's contingency expected
/// only the replace/clear cases to hit an EF in-memory translation error on the
/// <c>ComplexProperty</c> predicate (<c>r.Source.Kind</c> / <c>r.Source.Id</c>), with the
/// "insert into an empty set" case surviving because it "needs no predicate match". In
/// practice EF's in-memory provider fails to <em>translate</em> the expression tree at all
/// once it references a <c>ComplexProperty</c> member — this happens at query-compile time,
/// before any row is examined, so it is unconditional on the table's contents. All six cases
/// below throw the identical <c>InvalidOperationException</c> ("Translation of member
/// 'Source' ... failed"), not just the two replace/clear ones. Per the brief: do not work
/// around this by filtering in memory inside the production handler, and do not switch the
/// handler to raw SQL — so every case here is <c>[Ignore]</c>d rather than deleted (keeping
/// the intended assertions as the spec for Task 3) and the real-seam coverage — all six
/// scenarios, not only replace/clear — lands in <c>SetComponentSystemTests</c> (real Postgres).</para></summary>
[TestClass]
public sealed class SetComponentSystemHandlerTests
{
    private const string InMemoryLimitation =
        "EF in-memory cannot translate the ComplexProperty predicate (r.Source.Kind / r.Source.Id) — " +
        "translation fails unconditionally, not just on the replace/clear paths anticipated by the " +
        "brief's contingency. Covered for real in SetComponentSystemTests (real Postgres).";

    private static readonly TenantId Tenant = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    // Fake clock, not TimeProvider.System — repo convention (TestClocks.cs in this project) and
    // the only way CreatedAt is assertable. A wall-clock read in a unit test is a flake vector.
    private static readonly FakeTimeProvider Clock =
        TestClocks.At(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero));

    private static CatalogDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("SetComponentSystemHandlerTests_" + Guid.NewGuid())
            .Options);

    private static (ITenantContext Tenant, ICurrentUser User) Ambient()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.Id.Returns(Tenant);
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(UserId);
        return (tenant, user);
    }

    [Ignore(InMemoryLimitation)]
    [TestMethod]
    public async Task Assigning_an_unassigned_component_inserts_the_edge_and_audits_creation()
    {
        await using var db = NewDb();
        var (tenant, user) = Ambient();
        var audit = Substitute.For<IAuditWriter>();
        var appId = Guid.NewGuid();
        var sysId = Guid.NewGuid();
        var handler = new SetComponentSystemHandler(Clock);

        var resp = await handler.Handle(
            new SetComponentSystemCommand(new EntityRef(EntityKind.Application, appId), sysId),
            "Payments Platform", db, tenant, user, audit, CancellationToken.None);

        Assert.AreEqual(sysId, resp.SystemId);
        Assert.AreEqual("Payments Platform", resp.SystemDisplayName);
        // Assert the persisted edge's shape, not just that a row exists.
        var edge = await db.Relationships.SingleAsync();
        Assert.AreEqual(appId, edge.Source.Id);
        Assert.AreEqual(EntityKind.Application, edge.Source.Kind);
        Assert.AreEqual(sysId, edge.Target.Id);
        Assert.AreEqual(EntityKind.System, edge.Target.Kind);
        Assert.AreEqual(RelationshipType.PartOf, edge.Type);
        Assert.AreEqual(RelationshipOrigin.Manual, edge.Origin);
        Assert.AreEqual(UserId, edge.CreatedByUserId);
        Assert.AreEqual(Clock.GetUtcNow(), edge.CreatedAt, "the injected clock must be the time source");
        // Audit must point at THIS edge, not merely carry the right action — a handler that
        // audits the wrong TargetId passes an action-only assertion.
        await audit.Received(1).AppendAsync(
            Arg.Is<AuditEntry>(e => e.Action == CatalogAuditActions.RelationshipCreated
                                    && e.TargetId == edge.Id.Value.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Ignore(InMemoryLimitation)]
    [TestMethod]
    public async Task Clearing_removes_the_edge_and_audits_removal()
    {
        await using var db = NewDb();
        var (tenant, user) = Ambient();
        var audit = Substitute.For<IAuditWriter>();
        var appId = Guid.NewGuid();
        var sysId = Guid.NewGuid();
        db.Relationships.Add(Relationship.CreateManual(
            new EntityRef(EntityKind.Application, appId), new EntityRef(EntityKind.System, sysId),
            RelationshipType.PartOf, UserId, Tenant, Clock));
        await db.SaveChangesAsync();
        var handler = new SetComponentSystemHandler(Clock);

        var resp = await handler.Handle(
            new SetComponentSystemCommand(new EntityRef(EntityKind.Application, appId), null),
            null, db, tenant, user, audit, CancellationToken.None);

        Assert.IsNull(resp.SystemId);
        Assert.IsNull(resp.SystemDisplayName);
        Assert.AreEqual(0, await db.Relationships.CountAsync());
        await audit.Received(1).AppendAsync(
            Arg.Is<AuditEntry>(e => e.Action == CatalogAuditActions.RelationshipRemoved),
            Arg.Any<CancellationToken>());
    }

    [Ignore(InMemoryLimitation)]
    [TestMethod]
    public async Task Re_assigning_to_the_same_system_writes_nothing_and_audits_nothing()
    {
        await using var db = NewDb();
        var (tenant, user) = Ambient();
        var audit = Substitute.For<IAuditWriter>();
        var svcId = Guid.NewGuid();
        var sysId = Guid.NewGuid();
        db.Relationships.Add(Relationship.CreateManual(
            new EntityRef(EntityKind.Service, svcId), new EntityRef(EntityKind.System, sysId),
            RelationshipType.PartOf, UserId, Tenant, Clock));
        await db.SaveChangesAsync();
        var handler = new SetComponentSystemHandler(Clock);

        var resp = await handler.Handle(
            new SetComponentSystemCommand(new EntityRef(EntityKind.Service, svcId), sysId),
            "Same System", db, tenant, user, audit, CancellationToken.None);

        Assert.AreEqual(sysId, resp.SystemId);
        Assert.AreEqual(1, await db.Relationships.CountAsync());
        await audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Ignore(InMemoryLimitation)]
    [TestMethod]
    public async Task Moving_between_systems_audits_BOTH_the_removal_and_the_creation()
    {
        // Without this case a bug that audits only the insert on a move ships undetected:
        // the other three tests each exercise a single-audit path.
        await using var db = NewDb();
        var (tenant, user) = Ambient();
        var audit = Substitute.For<IAuditWriter>();
        var appId = Guid.NewGuid();
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        db.Relationships.Add(Relationship.CreateManual(
            new EntityRef(EntityKind.Application, appId), new EntityRef(EntityKind.System, from),
            RelationshipType.PartOf, UserId, Tenant, Clock));
        await db.SaveChangesAsync();
        var handler = new SetComponentSystemHandler(Clock);

        var resp = await handler.Handle(
            new SetComponentSystemCommand(new EntityRef(EntityKind.Application, appId), to),
            "Destination System", db, tenant, user, audit, CancellationToken.None);

        Assert.AreEqual(to, resp.SystemId);
        var edge = await db.Relationships.SingleAsync();
        Assert.AreEqual(to, edge.Target.Id, "at-most-one: the surviving edge points at the destination");
        // The removal audit must name the edge that was actually deleted (its Data carries the
        // OLD target). Asserting the action alone lets "audit the wrong edge" survive.
        await audit.Received(1).AppendAsync(
            Arg.Is<AuditEntry>(e => e.Action == CatalogAuditActions.RelationshipRemoved
                                    && e.Data != null
                                    && e.Data["targetId"] == from.ToString()),
            Arg.Any<CancellationToken>());
        await audit.Received(1).AppendAsync(
            Arg.Is<AuditEntry>(e => e.Action == CatalogAuditActions.RelationshipCreated),
            Arg.Any<CancellationToken>());
    }

    [Ignore(InMemoryLimitation)]
    [TestMethod]
    public async Task Clearing_an_already_unassigned_component_writes_and_audits_nothing()
    {
        await using var db = NewDb();
        var (tenant, user) = Ambient();
        var audit = Substitute.For<IAuditWriter>();
        var handler = new SetComponentSystemHandler(Clock);

        var resp = await handler.Handle(
            new SetComponentSystemCommand(new EntityRef(EntityKind.Application, Guid.NewGuid()), null),
            null, db, tenant, user, audit, CancellationToken.None);

        Assert.IsNull(resp.SystemId);
        Assert.AreEqual(0, await db.Relationships.CountAsync());
        await audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Ignore(InMemoryLimitation)]
    [TestMethod]
    public async Task A_write_for_one_component_leaves_another_components_membership_alone()
    {
        // Kills the mutation "drop the Source.Id filter" — which would wipe every
        // component's membership in the tenant on any single PUT.
        await using var db = NewDb();
        var (tenant, user) = Ambient();
        var audit = Substitute.For<IAuditWriter>();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var sysA = Guid.NewGuid();
        var sysB = Guid.NewGuid();
        db.Relationships.Add(Relationship.CreateManual(
            new EntityRef(EntityKind.Application, theirs), new EntityRef(EntityKind.System, sysA),
            RelationshipType.PartOf, UserId, Tenant, Clock));
        await db.SaveChangesAsync();
        var handler = new SetComponentSystemHandler(Clock);

        await handler.Handle(
            new SetComponentSystemCommand(new EntityRef(EntityKind.Application, mine), sysB),
            "Mine", db, tenant, user, audit, CancellationToken.None);

        // Assert edge IDENTITY, not just the count: "delete theirs and insert two for mine"
        // also totals 2, so a count-only oracle lets that mutation live.
        var edges = await db.Relationships.ToListAsync();
        Assert.AreEqual(2, edges.Count);
        Assert.ContainsSingle(edges.Where(r => r.Source.Id == theirs && r.Target.Id == sysA),
            "the other component's edge must survive untouched");
        Assert.ContainsSingle(edges.Where(r => r.Source.Id == mine && r.Target.Id == sysB));
        await audit.DidNotReceive().AppendAsync(
            Arg.Is<AuditEntry>(e => e.Action == CatalogAuditActions.RelationshipRemoved),
            Arg.Any<CancellationToken>());
    }
}
