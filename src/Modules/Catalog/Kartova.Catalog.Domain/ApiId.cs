namespace Kartova.Catalog.Domain;

public readonly record struct ApiId(Guid Value)
{
    public static ApiId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
