using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

public sealed class Service : ITenantOwned, ITeamScopedResource
{
    private const int MaxEndpoints = 50;

    // Plain-Guid backing field for the PK so EF translates ORDER BY / WHERE without
    // a value converter (same pattern as Application._id).
    private Guid _id;
    private readonly List<ServiceEndpoint> _endpoints = new();

    public ServiceId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid TeamId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public HealthStatus Health { get; private set; } = HealthStatus.Unknown;
    public IReadOnlyList<ServiceEndpoint> Endpoints => _endpoints;
    public uint Version { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    private Service() { }   // EF

    private Service(
        ServiceId id, TenantId tenantId, string displayName, string description,
        Guid createdByUserId, Guid teamId, IEnumerable<ServiceEndpoint> endpoints, DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        DisplayName = displayName;
        Description = description;
        CreatedByUserId = createdByUserId;
        TeamId = teamId;
        CreatedAt = createdAt;
        _endpoints.AddRange(endpoints);
    }

    public static Service Create(
        string displayName, string description, Guid createdByUserId, Guid teamId,
        IEnumerable<ServiceEndpoint> endpoints, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(displayName, description, createdByUserId, teamId, endpoints, tenantId, clock.GetUtcNow());
    }

    /// <summary>Overload taking an explicit <paramref name="createdAt"/> — used by
    /// seeding/test fixtures that need deterministic ordering.</summary>
    public static Service Create(
        string displayName, string description, Guid createdByUserId, Guid teamId,
        IEnumerable<ServiceEndpoint> endpoints, TenantId tenantId, DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));
        if (teamId == Guid.Empty)
            throw new ArgumentException("teamId is required.", nameof(teamId));

        var list = endpoints?.ToList() ?? new List<ServiceEndpoint>();
        if (list.Count > MaxEndpoints)
            throw new ArgumentException($"a service may have at most {MaxEndpoints} endpoints.", nameof(endpoints));

        return new Service(ServiceId.New(), tenantId, displayName, description, createdByUserId, teamId, list, createdAt);
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Service display name must not be empty.", nameof(displayName));
        if (displayName.Length > 128)
            throw new ArgumentException("Service display name must be <= 128 characters.", nameof(displayName));
    }

    private static void ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Service description must not be empty.", nameof(description));
        if (description.Length > 4096)
            throw new ArgumentException("Service description must be <= 4096 characters.", nameof(description));
    }
}
