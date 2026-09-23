namespace Kartova.Catalog.Domain;

public readonly record struct EnvironmentId(Guid Value)
{
    public static EnvironmentId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
