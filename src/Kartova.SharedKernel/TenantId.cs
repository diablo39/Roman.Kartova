namespace Kartova.SharedKernel;

/// <summary>
/// Strongly-typed tenant identifier. Used as a filter key for multi-tenancy
/// (PostgreSQL RLS, Elasticsearch tenant routing). Immutable value object.
/// </summary>
public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());

    public static TenantId Parse(string value)
    {
        if (!Guid.TryParse(value, out var guid))
        {
            throw new FormatException($"Invalid TenantId: '{value}'");
        }
        return new TenantId(guid);
    }

    public override string ToString() => Value.ToString();
}
