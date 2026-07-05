using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Tests;

[TestClass]
public class RelationshipTests
{
    private static EntityRef Svc(Guid id) => new(EntityKind.Service, id);
    private static EntityRef App(Guid id) => new(EntityKind.Application, id);
    private static EntityRef Api(Guid id) => new(EntityKind.Api, id);
    private static TenantId T() => new(Guid.NewGuid());

    [TestMethod]
    public void EntityRef_rejects_empty_id()
        => Assert.ThrowsExactly<ArgumentException>(() => new EntityRef(EntityKind.Service, Guid.Empty));

    [TestMethod]
    public void EntityRef_rejects_undefined_kind()
        => Assert.ThrowsExactly<ArgumentException>(() => new EntityRef((EntityKind)99, Guid.NewGuid()));

    [TestMethod]
    public void EntityRef_value_equality_holds()
    {
        var id = Guid.NewGuid();
        Assert.AreEqual(new EntityRef(EntityKind.Api, id), new EntityRef(EntityKind.Api, id));
        Assert.AreNotEqual(new EntityRef(EntityKind.Api, id), new EntityRef(EntityKind.Service, id));
    }

    [TestMethod]
    public void IsCreatable_is_dependsOn_instanceOf_provides_consumes()
    {
        foreach (var t in new[] { RelationshipType.DependsOn, RelationshipType.InstanceOf,
                     RelationshipType.ProvidesApiFor, RelationshipType.ConsumesApiFrom })
            Assert.IsTrue(RelationshipTypeRules.IsCreatable(t), $"{t} must be creatable");

        foreach (var t in new[] { RelationshipType.PublishesTo, RelationshipType.SubscribesFrom, RelationshipType.DeployedOn })
            Assert.IsFalse(RelationshipTypeRules.IsCreatable(t), $"{t} must not be creatable yet");
    }

    [TestMethod]
    // depends-on: any → any (incl. Api endpoints)
    [DataRow(RelationshipType.DependsOn, EntityKind.Service, EntityKind.Service, true)]
    [DataRow(RelationshipType.DependsOn, EntityKind.Application, EntityKind.Api, true)]
    // instance-of: Service → Application ONLY
    [DataRow(RelationshipType.InstanceOf, EntityKind.Service, EntityKind.Application, true)]
    [DataRow(RelationshipType.InstanceOf, EntityKind.Application, EntityKind.Service, false)]
    [DataRow(RelationshipType.InstanceOf, EntityKind.Service, EntityKind.Api, false)]
    [DataRow(RelationshipType.InstanceOf, EntityKind.Service, EntityKind.Service, false)]
    // provides-api-for: {App,Service} → Api ONLY
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Application, EntityKind.Api, true)]
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Service, EntityKind.Api, true)]
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Service, EntityKind.Application, false)]
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Api, EntityKind.Application, false)]
    // consumes-api-from: {App,Service} → Api ONLY
    [DataRow(RelationshipType.ConsumesApiFrom, EntityKind.Service, EntityKind.Api, true)]
    [DataRow(RelationshipType.ConsumesApiFrom, EntityKind.Application, EntityKind.Api, true)]
    [DataRow(RelationshipType.ConsumesApiFrom, EntityKind.Api, EntityKind.Service, false)]
    [DataRow(RelationshipType.ConsumesApiFrom, EntityKind.Service, EntityKind.Service, false)]
    // non-creatable type hits the default arm → false
    [DataRow(RelationshipType.PublishesTo, EntityKind.Service, EntityKind.Service, false)]
    public void IsAllowedPair_matrix(RelationshipType type, EntityKind source, EntityKind target, bool expected)
        => Assert.AreEqual(expected, RelationshipTypeRules.IsAllowedPair(type, source, target));

    [TestMethod]
    public void CreateManual_dependsOn_sets_fields_and_manual_origin()
    {
        var src = Svc(Guid.NewGuid());
        var tgt = Svc(Guid.NewGuid());
        var creator = Guid.NewGuid();
        var rel = Relationship.CreateManual(src, tgt, RelationshipType.DependsOn, creator, T(), TimeProvider.System);

        Assert.AreEqual(src, rel.Source);
        Assert.AreEqual(tgt, rel.Target);
        Assert.AreEqual(RelationshipType.DependsOn, rel.Type);
        Assert.AreEqual(RelationshipOrigin.Manual, rel.Origin);
        Assert.AreEqual(creator, rel.CreatedByUserId);
        Assert.AreNotEqual(Guid.Empty, rel.Id.Value);
    }

    [TestMethod]
    [DataRow(RelationshipType.InstanceOf, EntityKind.Service, EntityKind.Application)]
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Service, EntityKind.Api)]
    [DataRow(RelationshipType.ConsumesApiFrom, EntityKind.Application, EntityKind.Api)]
    public void CreateManual_valid_pair_sets_type(RelationshipType type, EntityKind sourceKind, EntityKind targetKind)
    {
        var rel = Relationship.CreateManual(new EntityRef(sourceKind, Guid.NewGuid()), new EntityRef(targetKind, Guid.NewGuid()),
            type, Guid.NewGuid(), T(), TimeProvider.System);
        Assert.AreEqual(type, rel.Type);
    }

    [TestMethod]
    public void CreateManual_rejects_self_reference()
    {
        var same = Svc(Guid.NewGuid());
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            same, same, RelationshipType.DependsOn, Guid.NewGuid(), T(), TimeProvider.System));
    }

    [TestMethod]
    public void CreateManual_rejects_non_creatable_type()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Svc(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.PublishesTo, Guid.NewGuid(), T(), TimeProvider.System));
        StringAssert.Contains(ex.Message, "not yet available");
    }

    [TestMethod]
    public void CreateManual_rejects_disallowed_pair_providesApiFor_api_to_application()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Api(Guid.NewGuid()), App(Guid.NewGuid()), RelationshipType.ProvidesApiFor, Guid.NewGuid(), T(), TimeProvider.System));
    }

    [TestMethod]
    public void CreateManual_rejects_empty_creator()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Svc(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.DependsOn, Guid.Empty, T(), TimeProvider.System));
    }
}
