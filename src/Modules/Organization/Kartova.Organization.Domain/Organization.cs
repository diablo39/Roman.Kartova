using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain;

public sealed class Organization : ITenantOwned
{
    public OrganizationId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Organization(OrganizationId id, TenantId tenantId, string name, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        CreatedAt = createdAt;
    }

    // EF constructor — Name is defensively initialized in case EF instantiates
    // without immediately rehydrating from a row. Mutation-survivor: EF Core
    // always sets backing fields via reflection from the row data, so removing
    // this initializer is observably equivalent. (slice-6 mutation report 2026-05-07)
    private Organization() { Name = string.Empty; }

    public static Organization Create(string name, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateName(name);
        var id = OrganizationId.New();
        // Per ADR-0011, one org = one tenant; tenant_id is the same GUID as the org id.
        var tenantId = new TenantId(id.Value);
        return new Organization(id, tenantId, name, clock.GetUtcNow());
    }

    public void Rename(string newName)
    {
        // mutation-survivor: pre-slice-6; killing requires a Rename invalid-name test
        // that wasn't in scope for slice 6. Carries forward to the next Organization slice.
        ValidateName(newName);
        Name = newName;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name must not be empty.", nameof(name));
        }
        if (name.Length > 100)
        {
            throw new ArgumentException("Organization name must be <= 100 characters.", nameof(name));
        }
    }
}
