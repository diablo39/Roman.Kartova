using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public class RelationshipTests
{
    [TestMethod]
    public void EntityRef_rejects_empty_id()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EntityRef(EntityKind.Service, Guid.Empty));
    }

    [TestMethod]
    public void EntityRef_rejects_undefined_kind()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EntityRef((EntityKind)99, Guid.NewGuid()));
    }

    [TestMethod]
    public void EntityRef_value_equality_holds()
    {
        var id = Guid.NewGuid();
        Assert.AreEqual(new EntityRef(EntityKind.Service, id), new EntityRef(EntityKind.Service, id));
        Assert.AreNotEqual(new EntityRef(EntityKind.Service, id), new EntityRef(EntityKind.Application, id));
    }

    [TestMethod]
    public void IsCreatable_only_dependsOn_and_partOf()
    {
        Assert.IsTrue(RelationshipTypeRules.IsCreatable(RelationshipType.DependsOn));
        Assert.IsTrue(RelationshipTypeRules.IsCreatable(RelationshipType.PartOf));
        foreach (var t in new[] { RelationshipType.ProvidesApiFor, RelationshipType.ConsumesApiFrom,
                     RelationshipType.PublishesTo, RelationshipType.SubscribesFrom, RelationshipType.DeployedOn })
            Assert.IsFalse(RelationshipTypeRules.IsCreatable(t), $"{t} must not be creatable in slice 1a");
    }

    // Non-creatable types hit the `_ => false` default arm (covers it so the mutant there is killed).
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Service, EntityKind.Application, false)]
    [DataRow(RelationshipType.PublishesTo, EntityKind.Service, EntityKind.Service, false)]

    [TestMethod]
    // depends-on: any of {App,Service} → any of {App,Service}
    [DataRow(RelationshipType.DependsOn, EntityKind.Service, EntityKind.Service, true)]
    [DataRow(RelationshipType.DependsOn, EntityKind.Application, EntityKind.Service, true)]
    [DataRow(RelationshipType.DependsOn, EntityKind.Service, EntityKind.Application, true)]
    [DataRow(RelationshipType.DependsOn, EntityKind.Application, EntityKind.Application, true)]
    // part-of: Service → Application ONLY
    [DataRow(RelationshipType.PartOf, EntityKind.Service, EntityKind.Application, true)]
    [DataRow(RelationshipType.PartOf, EntityKind.Application, EntityKind.Service, false)]
    [DataRow(RelationshipType.PartOf, EntityKind.Service, EntityKind.Service, false)]
    [DataRow(RelationshipType.PartOf, EntityKind.Application, EntityKind.Application, false)]
    public void IsAllowedPair_matrix(RelationshipType type, EntityKind source, EntityKind target, bool expected)
    {
        Assert.AreEqual(expected, RelationshipTypeRules.IsAllowedPair(type, source, target));
    }

    private static EntityRef Svc(Guid id) => new(EntityKind.Service, id);
    private static EntityRef App(Guid id) => new(EntityKind.Application, id);

    [TestMethod]
    public void CreateManual_dependsOn_sets_fields_and_manual_origin()
    {
        var src = Svc(Guid.NewGuid());
        var tgt = Svc(Guid.NewGuid());
        var creator = Guid.NewGuid();
        var rel = Relationship.CreateManual(src, tgt, RelationshipType.DependsOn, creator,
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System);

        Assert.AreEqual(src, rel.Source);
        Assert.AreEqual(tgt, rel.Target);
        Assert.AreEqual(RelationshipType.DependsOn, rel.Type);
        Assert.AreEqual(RelationshipOrigin.Manual, rel.Origin);
        Assert.AreEqual(creator, rel.CreatedByUserId);
        Assert.AreNotEqual(Guid.Empty, rel.Id.Value);
    }

    [TestMethod]
    public void CreateManual_partOf_service_to_application_is_valid()
    {
        var rel = Relationship.CreateManual(Svc(Guid.NewGuid()), App(Guid.NewGuid()),
            RelationshipType.PartOf, Guid.NewGuid(),
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System);
        Assert.AreEqual(RelationshipType.PartOf, rel.Type);
    }

    [TestMethod]
    public void CreateManual_rejects_self_reference()
    {
        var same = Svc(Guid.NewGuid());
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            same, same, RelationshipType.DependsOn, Guid.NewGuid(),
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System));
    }

    [TestMethod]
    public void CreateManual_rejects_non_creatable_type()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Svc(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.PublishesTo, Guid.NewGuid(),
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System));
        // Assert the not-creatable guard fired (its message), not the bad-pair guard below it —
        // both throw ArgumentException, so the type-only check let the line-32 throw-removal mutant survive.
        StringAssert.Contains(ex.Message, "not yet available");
    }

    [TestMethod]
    public void CreateManual_rejects_disallowed_pair_partOf_app_to_service()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            App(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.PartOf, Guid.NewGuid(),
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System));
    }

    [TestMethod]
    public void CreateManual_rejects_empty_creator()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Svc(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.DependsOn, Guid.Empty,
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System));
    }
}
