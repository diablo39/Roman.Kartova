namespace Kartova.SharedKernel.Multitenancy;

public sealed class TenantContextAccessor : ITenantContext
{
    private TenantId _id = TenantId.Empty;
    private IReadOnlyCollection<string> _roles = Array.Empty<string>();
    private bool _populated;

    public TenantId Id => _id;
    public bool IsTenantScoped => _populated && _id != TenantId.Empty;
    public IReadOnlyCollection<string> Roles => _roles;

    public void Populate(TenantId id, IReadOnlyCollection<string> roles)
    {
        _id = id;
        _roles = roles ?? Array.Empty<string>();
        _populated = true;
    }

    public void Clear()
    {
        _id = TenantId.Empty;
        _roles = Array.Empty<string>();
        _populated = false;
    }
}
