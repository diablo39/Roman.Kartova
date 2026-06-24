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
}
