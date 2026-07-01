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
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Lifecycle Lifecycle { get; private set; } = Lifecycle.Active;
    public DateTimeOffset? SunsetDate { get; private set; }
    public Guid? SuccessorApplicationId { get; private set; }
    public Guid TeamId { get; private set; }
    public uint Version { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    private Application(
        ApplicationId id,
        TenantId tenantId,
        string displayName,
        string description,
        Guid createdByUserId,
        Guid teamId,
        DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        DisplayName = displayName;
        Description = description;
        CreatedByUserId = createdByUserId;
        TeamId = teamId;
        CreatedAt = createdAt;
    }

    // EF constructor
    private Application() { }

    public static Application Create(string displayName, string description, Guid createdByUserId, Guid teamId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(displayName, description, createdByUserId, teamId, tenantId, clock.GetUtcNow());
    }

    /// <summary>
    /// Overload that accepts an explicit <paramref name="createdAt"/> timestamp.
    /// Used by migration/seeding code and integration-test fixtures that need
    /// deterministic ordering without sleeping between inserts.
    /// </summary>
    public static Application Create(
        string displayName,
        string description,
        Guid createdByUserId,
        Guid teamId,
        TenantId tenantId,
        DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));
        }
        if (teamId == Guid.Empty)
        {
            throw new ArgumentException("teamId is required.", nameof(teamId));
        }

        return new Application(
            ApplicationId.New(),
            tenantId,
            displayName,
            description,
            createdByUserId,
            teamId,
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

    public void Deprecate(DateTimeOffset sunsetDate, TimeProvider clock, Guid? successorApplicationId = null)
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

        RejectSelfSuccessor(successorApplicationId);

        Lifecycle = Lifecycle.Deprecated;
        SunsetDate = sunsetDate;
        SuccessorApplicationId = successorApplicationId;
    }

    /// <summary>
    /// Sets or clears the successor reference while Deprecated (ADR-0110 §5.3).
    /// App→App only — a single <see cref="Guid"/>, not polymorphic. Existence
    /// validation is a handler concern (C3/C4); the domain has no DB access.
    /// </summary>
    public void SetSuccessor(Guid? successorApplicationId)
    {
        if (Lifecycle != Lifecycle.Deprecated)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, nameof(SetSuccessor), SunsetDate);
        }

        RejectSelfSuccessor(successorApplicationId);
        SuccessorApplicationId = successorApplicationId;
    }

    /// <summary>
    /// Transitions a Deprecated application to Decommissioned. Blocked before
    /// <see cref="SunsetDate"/> unless <paramref name="allowBeforeSunset"/> is set,
    /// letting an admin bypass the sunset-date guard (ADR-0073 §5.2).
    /// </summary>
    public void Decommission(TimeProvider clock, bool allowBeforeSunset = false)
    {
        if (Lifecycle != Lifecycle.Deprecated)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Decommission), SunsetDate);
        }

        if (!allowBeforeSunset && clock.GetUtcNow() < SunsetDate!.Value)
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
        SuccessorApplicationId = null;
    }

    /// <summary>
    /// Reassigns this application to another team. <c>TeamId</c> is required (the
    /// owner) — there is no unassign (ADR-0103: no ownerless apps). Reassignment is
    /// blocked on Decommissioned (terminal-write guard, consistent with EditMetadata).
    /// </summary>
    public void AssignTeam(Guid teamId)
    {
        if (Lifecycle == Lifecycle.Decommissioned)
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

    private void RejectSelfSuccessor(Guid? successorApplicationId)
    {
        if (successorApplicationId == _id)
        {
            throw new ArgumentException("An application cannot be its own successor.", nameof(successorApplicationId));
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
        if (description.Length > 4096)
        {
            throw new ArgumentException("Application description must be <= 4096 characters.", nameof(description));
        }
    }
}
