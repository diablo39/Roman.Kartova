namespace Kartova.Catalog.Domain;

public readonly record struct ApplicationId(Guid Value)
{
    public static ApplicationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
