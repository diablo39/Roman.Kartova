namespace Kartova.Catalog.Domain;

public readonly record struct EntityRef
{
    public EntityKind Kind { get; }
    public Guid Id { get; }

    public EntityRef(EntityKind kind, Guid id)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("unknown entity kind", nameof(kind));
        if (id == Guid.Empty) throw new ArgumentException("entity id required", nameof(id));
        Kind = kind;
        Id = id;
    }
}
