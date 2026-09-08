using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

/// <summary>
/// Infrastructure catalog entity (ADR-0111 amendment). First-class, tenant-owned, team-owned.
/// One unified aggregate keyed by <see cref="Type"/>; type-variant attributes live in the
/// opaque <see cref="Attributes"/> jsonb payload, not in columns. <c>SystemId</c> stays null
/// in this slice — there is no write path yet (follow-up). The Postgres <c>xmin</c>
/// concurrency token maps to <c>Xmin</c>.
/// </summary>
public sealed class InfrastructureResource : ITenantOwned, ITeamScopedResource
{
    private Guid _id;   // plain-Guid PK backing field (same pattern as Application/Service/Api)

    public InfrastructureId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public InfrastructureType Type { get; private set; }
    public Guid? SystemId { get; private set; }
    public string Attributes { get; private set; } = string.Empty;
    public Guid TeamId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Xmin { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    private InfrastructureResource() { }   // EF

    private InfrastructureResource(
        InfrastructureId id, TenantId tenantId, string displayName, string description, InfrastructureType type,
        string attributesJson, Guid createdByUserId, Guid teamId, DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        DisplayName = displayName;
        Description = description;
        Type = type;
        SystemId = null;
        Attributes = attributesJson;
        CreatedByUserId = createdByUserId;
        TeamId = teamId;
        CreatedAt = createdAt;
    }

    public static InfrastructureResource Create(
        string displayName, string description, InfrastructureType type, string attributesJson,
        Guid createdByUserId, Guid teamId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(displayName, description, type, attributesJson, createdByUserId, teamId, tenantId, clock.GetUtcNow());
    }

    /// <summary>Overload taking an explicit <paramref name="createdAt"/> — for seed/test fixtures.</summary>
    public static InfrastructureResource Create(
        string displayName, string description, InfrastructureType type, string attributesJson,
        Guid createdByUserId, Guid teamId, TenantId tenantId, DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (!Enum.IsDefined(type))
            throw new ArgumentException("Unknown infrastructure type.", nameof(type));
        ValidateAttributes(attributesJson);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));
        if (teamId == Guid.Empty)
            throw new ArgumentException("teamId is required.", nameof(teamId));

        return new InfrastructureResource(InfrastructureId.New(), tenantId, displayName, description, type, attributesJson, createdByUserId, teamId, createdAt);
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Infrastructure display name must not be empty.", nameof(displayName));
        if (displayName.Length > 128)
            throw new ArgumentException("Infrastructure display name must be <= 128 characters.", nameof(displayName));
    }

    private static void ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Infrastructure description must not be empty.", nameof(description));
        if (description.Length > 4096)
            throw new ArgumentException("Infrastructure description must be <= 4096 characters.", nameof(description));
    }

    private static void ValidateAttributes(string attributesJson)
    {
        if (string.IsNullOrWhiteSpace(attributesJson))
            throw new ArgumentException("Infrastructure attributes must not be empty.", nameof(attributesJson));
    }
}
