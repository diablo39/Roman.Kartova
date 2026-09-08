namespace Kartova.Catalog.Domain;

public readonly record struct InfrastructureId(Guid Value)
{
    public static InfrastructureId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
