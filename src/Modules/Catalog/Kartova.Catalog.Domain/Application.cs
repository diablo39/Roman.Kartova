using System.Text.RegularExpressions;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

public sealed partial class Application : ITenantOwned
{
    // Backing field for the primary key — stored as a plain Guid so EF Core can
    // translate ORDER BY / WHERE expressions without going through the value
    // converter. The domain-typed Id property is computed from this backing field.
    // Using a backing field eliminates the .Value member access that EF Core's
    // LINQ translator cannot push down to SQL when the property has a converter.
    private Guid _id;

    /// <summary>Domain-typed identifier. EF maps the <c>_id</c> backing field.</summary>
    public ApplicationId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Lifecycle Lifecycle { get; private set; } = Lifecycle.Active;
    public DateTimeOffset? SunsetDate { get; private set; }
    public uint Version { get; private set; }

    private Application(
        ApplicationId id,
        TenantId tenantId,
        string name,
        string displayName,
        string description,
        Guid ownerUserId,
        DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        Name = name;
        DisplayName = displayName;
        Description = description;
        OwnerUserId = ownerUserId;
        CreatedAt = createdAt;
    }

    // EF constructor
    private Application() { }

    public static Application Create(string name, string displayName, string description, Guid ownerUserId, TenantId tenantId)
        => Create(name, displayName, description, ownerUserId, tenantId, DateTimeOffset.UtcNow);

    /// <summary>
    /// Overload that accepts an explicit <paramref name="createdAt"/> timestamp.
    /// Used by migration/seeding code and integration-test fixtures that need
    /// deterministic ordering without sleeping between inserts.
    /// </summary>
    public static Application Create(
        string name,
        string displayName,
        string description,
        Guid ownerUserId,
        TenantId tenantId,
        DateTimeOffset createdAt)
    {
        ValidateName(name);
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("ownerUserId is required.", nameof(ownerUserId));
        }

        return new Application(
            ApplicationId.New(),
            tenantId,
            name,
            displayName,
            description,
            ownerUserId,
            createdAt);
    }

    public void EditMetadata(string displayName, string description)
    {
        if (Lifecycle == Lifecycle.Decommissioned)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, "EditMetadata");
        }

        ValidateDisplayName(displayName);
        ValidateDescription(description);

        DisplayName = displayName;
        Description = description;
    }

    public void Deprecate(DateTimeOffset sunsetDate, TimeProvider clock)
    {
        if (Lifecycle != Lifecycle.Active)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, "Deprecate", SunsetDate);
        }

        if (sunsetDate <= clock.GetUtcNow())
        {
            throw new ArgumentException(
                "sunsetDate must be in the future.", nameof(sunsetDate));
        }

        Lifecycle = Lifecycle.Deprecated;
        SunsetDate = sunsetDate;
    }

    public void Decommission(TimeProvider clock)
    {
        if (Lifecycle != Lifecycle.Deprecated)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, "Decommission", SunsetDate);
        }

        if (clock.GetUtcNow() < SunsetDate!.Value)
        {
            throw new InvalidLifecycleTransitionException(
                Lifecycle, "Decommission", SunsetDate, reason: "before-sunset-date");
        }

        Lifecycle = Lifecycle.Decommissioned;
    }

    // Mirrors the SPA's zod rule so the SPA check is UX-only and the server is the source of truth.
    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Application name must not be empty.", nameof(name));
        }
        if (name.Length > 256)
        {
            throw new ArgumentException("Application name must be <= 256 characters.", nameof(name));
        }
        if (!KebabCase().IsMatch(name))
        {
            throw new ArgumentException(
                "Application name must be lowercase kebab-case (e.g. payment-gateway).",
                nameof(name));
        }
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Application display name must not be empty.", nameof(displayName));
        }
        if (displayName.Length > 128)
        {
            throw new ArgumentException("Application display name must be <= 128 characters.", nameof(displayName));
        }
    }

    private static void ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Application description must not be empty.", nameof(description));
        }
    }
}
