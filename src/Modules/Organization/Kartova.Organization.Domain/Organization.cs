using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain;

public sealed class Organization : ITenantOwned
{
    public OrganizationId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; }
    public string? Description { get; private set; }
    public OrgLogo? Logo { get; private set; }
    public string DefaultTimeZone { get; private set; } = "UTC";
    public DateTimeOffset CreatedAt { get; private set; }

    private Organization(OrganizationId id, TenantId tenantId, string displayName, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        DisplayName = displayName;
        CreatedAt = createdAt;
    }

    // EF constructor — DisplayName is defensively initialized in case EF instantiates
    // without immediately rehydrating from a row. Mutation-survivor: EF Core
    // always sets backing fields via reflection from the row data, so removing
    // this initializer is observably equivalent. (slice-6 mutation report 2026-05-07)
    private Organization() { DisplayName = string.Empty; }

    public static Organization Create(string name, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateDisplayName(name);
        var id = OrganizationId.New();
        // Per ADR-0011, one org = one tenant; tenant_id is the same GUID as the org id.
        var tenantId = new TenantId(id.Value);
        return new Organization(id, tenantId, name, clock.GetUtcNow());
    }

    public void Rename(string newName)
    {
        // mutation-survivor: pre-slice-6; killing requires a Rename invalid-name test
        // that wasn't in scope for slice 6. Carries forward to the next Organization slice.
        ValidateDisplayName(newName);
        DisplayName = newName;
    }

    public void UpdateProfile(string displayName, string? description, string defaultTimeZone)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        ValidateTimeZone(defaultTimeZone);
        DisplayName = displayName;
        Description = description;
        DefaultTimeZone = defaultTimeZone;
    }

    public void SetLogo(OrgLogo logo)
    {
        ArgumentNullException.ThrowIfNull(logo);
        Logo = logo;
    }

    public void ClearLogo() => Logo = null;

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Organization name must not be empty.", nameof(displayName));
        }
        if (displayName.Length > 100)
        {
            throw new ArgumentException("Organization name must be <= 100 characters.", nameof(displayName));
        }
    }

    private static void ValidateDescription(string? description)
    {
        if (description is { Length: > 1024 }) throw new ArgumentException("Description must be <= 1024 characters.", nameof(description));
    }

    private static void ValidateTimeZone(string tz)
    {
        if (string.IsNullOrWhiteSpace(tz)) throw new ArgumentException("Time-zone required.", nameof(tz));
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
            throw new ArgumentException("Unknown IANA time-zone id.", nameof(tz));
    }
}
