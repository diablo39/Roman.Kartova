namespace Kartova.Catalog.Domain;

public readonly record struct CatalogSystemId(Guid Value)
{
    public static CatalogSystemId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
