namespace Kartova.SharedKernel.Multitenancy;

public readonly record struct TenantId(Guid Value)
{
    public static readonly TenantId Empty = new(Guid.Empty);

    public static TenantId Parse(string s) =>
        new(Guid.Parse(s));

    public static bool TryParse(string? s, out TenantId tenantId)
    {
        if (Guid.TryParse(s, out var g))
        {
            tenantId = new TenantId(g);
            return true;
        }
        tenantId = Empty;
        return false;
    }

    public override string ToString() => Value.ToString();
}
