using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

public sealed class Application : ITenantOwned, ITeamScopedResource
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
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Lifecycle Lifecycle { get; private set; } = Lifecycle.Active;
    public DateTimeOffset? SunsetDate { get; private set; }
    public Guid? TeamId { get; private set; }
    public uint Version { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    private Application(
        ApplicationId id,
        TenantId tenantId,
        string displayName,
        string description,
        Guid ownerUserId,
        DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        DisplayName = displayName;
        Description = description;
        OwnerUserId = ownerUserId;
        CreatedAt = createdAt;
    }

    // EF constructor
    private Application() { }

    public static Application Create(string displayName, string description, Guid ownerUserId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(displayName, description, ownerUserId, tenantId, clock.GetUtcNow());
    }

    /// <summary>
    /// Overload that accepts an explicit <paramref name="createdAt"/> timestamp.
    /// Used by migration/seeding code and integration-test fixtures that need
    /// deterministic ordering without sleeping between inserts.
    /// </summary>
    public static Application Create(
        string displayName,
        string description,
        Guid ownerUserId,
        TenantId tenantId,
        DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("ownerUserId is required.", nameof(ownerUserId));
        }

        return new Application(
            ApplicationId.New(),
            tenantId,
            displayName,
            description,
            ownerUserId,
            createdAt);
    }

    public void EditMetadata(string displayName, string description)
    {
        if (Lifecycle == Lifecycle.Decommissioned)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, nameof(EditMetadata));
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
            throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Deprecate), SunsetDate);
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
            throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Decommission), SunsetDate);
        }

        if (clock.GetUtcNow() < SunsetDate!.Value)
        {
            throw new InvalidLifecycleTransitionException(
                Lifecycle, nameof(Decommission), SunsetDate, reason: "before-sunset-date");
        }

        Lifecycle = Lifecycle.Decommissioned;
    }

    public void Reactivate()
    {
        if (Lifecycle != Lifecycle.Deprecated && Lifecycle != Lifecycle.Decommissioned)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Reactivate));
        }

        Lifecycle = Lifecycle.Active;
        SunsetDate = null;
    }

    /// <summary>
    /// Assigns this application to a team (or unassigns when <paramref name="teamId"/> is null).
    /// Reassigning to a non-null team is blocked on Decommissioned (terminal-write guard,
    /// consistent with EditMetadata). Unassigning (null) is allowed on any lifecycle so
    /// OrgAdmin can release Decommissioned apps from a team before deleting the team —
    /// without this carve-out, a team that ever owned an app since-decommissioned would
    /// be undeletable forever (slice-8 boundary-review fix).
    /// </summary>
    public void AssignTeam(Guid? teamId)
    {
        if (teamId is not null && Lifecycle == Lifecycle.Decommissioned)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, nameof(AssignTeam));
        }

        TeamId = teamId;
    }

    public void UnDecommission(DateTimeOffset newSunsetDate, TimeProvider clock)
    {
        if (Lifecycle != Lifecycle.Decommissioned)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, nameof(UnDecommission), SunsetDate);
        }

        if (newSunsetDate <= clock.GetUtcNow())
        {
            throw new ArgumentException("sunsetDate must be in the future.", nameof(newSunsetDate));
        }

        Lifecycle = Lifecycle.Deprecated;
        SunsetDate = newSunsetDate;
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
