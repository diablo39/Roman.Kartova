namespace Kartova.Catalog.Domain;

public readonly record struct ServiceId(Guid Value)
{
    public static ServiceId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
