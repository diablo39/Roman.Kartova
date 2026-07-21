using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

/// <summary>
/// System catalog grouping entity (ADR-0111 amendment, E-03.F-03). A team-stewarded
/// grouping node that {Application, Service} components can be assigned to via the
/// <c>PartOf</c> relationship edge (see <see cref="RelationshipTypeRules"/>). Mirrors
/// the <see cref="Api"/> aggregate's structure; <c>TeamId</c> here is the steward team
/// (curates the grouping) — member components keep their own team ownership.
/// Named <c>CatalogSystem</c> (not <c>System</c>) to avoid shadowing the BCL <c>System</c> namespace.
/// </summary>
public sealed class CatalogSystem : ITenantOwned, ITeamScopedResource
{
    private Guid _id;   // plain-Guid PK backing field (same pattern as Api/Application/Service)

    public CatalogSystemId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Xmin { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    private CatalogSystem() { }   // EF

    private CatalogSystem(
        CatalogSystemId id, TenantId tenantId, string displayName, string? description,
        Guid createdByUserId, Guid teamId, DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        DisplayName = displayName;
        Description = description;
        CreatedByUserId = createdByUserId;
        TeamId = teamId;
        CreatedAt = createdAt;
    }

    public static CatalogSystem Create(
        string displayName, string? description, Guid createdByUserId, Guid teamId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(displayName, description, createdByUserId, teamId, tenantId, clock.GetUtcNow());
    }

    /// <summary>Overload taking an explicit <paramref name="createdAt"/> — for seed/test fixtures.</summary>
    public static CatalogSystem Create(
        string displayName, string? description, Guid createdByUserId, Guid teamId, TenantId tenantId, DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));
        if (teamId == Guid.Empty)
            throw new ArgumentException("teamId is required.", nameof(teamId));

        return new CatalogSystem(CatalogSystemId.New(), tenantId, displayName, description, createdByUserId, teamId, createdAt);
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("System display name must not be empty.", nameof(displayName));
        if (displayName.Length > 128)
            throw new ArgumentException("System display name must be <= 128 characters.", nameof(displayName));
    }

    private static void ValidateDescription(string? description)
    {
        if (description is null) return;
        if (description.Length > 4096)
            throw new ArgumentException("System description must be <= 4096 characters.", nameof(description));
    }
}
