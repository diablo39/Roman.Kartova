using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

public sealed class Relationship : ITenantOwned
{
    private Guid _id;

    public RelationshipId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public EntityRef Source { get; private set; }
    public EntityRef Target { get; private set; }
    public RelationshipType Type { get; private set; }
    public RelationshipOrigin Origin { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Relationship() { } // EF

    public static Relationship CreateManual(
        EntityRef source, EntityRef target, RelationshipType type,
        Guid createdByUserId, TenantId tenantId, TimeProvider clock)
        => CreateManual(source, target, type, createdByUserId, tenantId, clock.GetUtcNow());

    public static Relationship CreateManual(
        EntityRef source, EntityRef target, RelationshipType type,
        Guid createdByUserId, TenantId tenantId, DateTimeOffset createdAt)
    {
        if (source == target)
            throw new ArgumentException("a relationship cannot reference the same entity as source and target", nameof(target));
        if (!RelationshipTypeRules.IsCreatable(type))
            throw new ArgumentException($"relationship type '{type}' is not yet available", nameof(type));
        if (!RelationshipTypeRules.IsAllowedPair(type, source.Kind, target.Kind))
            throw new ArgumentException($"'{type}' is not valid from {source.Kind} to {target.Kind}", nameof(type));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId required", nameof(createdByUserId));

        return new Relationship
        {
            _id = RelationshipId.New().Value,
            TenantId = tenantId,
            Source = source,
            Target = target,
            Type = type,
            Origin = RelationshipOrigin.Manual,
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt,
        };
    }
}
