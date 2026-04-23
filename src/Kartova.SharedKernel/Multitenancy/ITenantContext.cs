namespace Kartova.SharedKernel.Multitenancy;

public interface ITenantContext
{
    TenantId Id { get; }
    bool IsTenantScoped { get; }
    IReadOnlyCollection<string> Roles { get; }

    void Populate(TenantId id, IReadOnlyCollection<string> roles);
    void Clear();
}
