using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

/// <summary>
/// Sync API catalog entity (ADR-0111). First-class, tenant-owned, team-owned. The
/// provider/instance links (implementedByApplicationId / Service.applicationId) and
/// derived exposure are follow-ups (spec §11) — this aggregate is the node only.
/// <c>Version</c> is the API version string (domain); the Postgres <c>xmin</c> concurrency
/// token maps to <c>Xmin</c> (renamed to avoid colliding with the domain Version field).
/// </summary>
public sealed class Api : ITenantOwned, ITeamScopedResource
{
    private Guid _id;   // plain-Guid PK backing field (same pattern as Application/Service)

    public ApiId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ApiStyle Style { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public string? SpecUrl { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Xmin { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    private Api() { }   // EF

    private Api(
        ApiId id, TenantId tenantId, string displayName, string description, ApiStyle style,
        string version, string? specUrl, Guid createdByUserId, Guid teamId, DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        DisplayName = displayName;
        Description = description;
        Style = style;
        Version = version;
        SpecUrl = specUrl;
        CreatedByUserId = createdByUserId;
        TeamId = teamId;
        CreatedAt = createdAt;
    }

    public static Api Create(
        string displayName, string description, ApiStyle style, string version, string? specUrl,
        Guid createdByUserId, Guid teamId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(displayName, description, style, version, specUrl, createdByUserId, teamId, tenantId, clock.GetUtcNow());
    }

    /// <summary>Overload taking an explicit <paramref name="createdAt"/> — for seed/test fixtures.</summary>
    public static Api Create(
        string displayName, string description, ApiStyle style, string version, string? specUrl,
        Guid createdByUserId, Guid teamId, TenantId tenantId, DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (!Enum.IsDefined(style))
            throw new ArgumentException("Unknown API style.", nameof(style));
        ValidateVersion(version);
        ValidateSpecUrl(specUrl);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));
        if (teamId == Guid.Empty)
            throw new ArgumentException("teamId is required.", nameof(teamId));

        return new Api(ApiId.New(), tenantId, displayName, description, style, version, specUrl, createdByUserId, teamId, createdAt);
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("API display name must not be empty.", nameof(displayName));
        if (displayName.Length > 128)
            throw new ArgumentException("API display name must be <= 128 characters.", nameof(displayName));
    }

    private static void ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("API description must not be empty.", nameof(description));
        if (description.Length > 4096)
            throw new ArgumentException("API description must be <= 4096 characters.", nameof(description));
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("API version must not be empty.", nameof(version));
        if (version.Length > 64)
            throw new ArgumentException("API version must be <= 64 characters.", nameof(version));
    }

    private static void ValidateSpecUrl(string? specUrl)
    {
        if (specUrl is null) return;
        if (specUrl.Length > 2048)
            throw new ArgumentException("API spec URL must be <= 2048 characters.", nameof(specUrl));
        // Strict absolute URI with host (spec URLs are real fetchable links — unlike the
        // relaxed ServiceEndpoint address rule granted by ADR-0111 §Decision 6, FU-4).
        if (!Uri.TryCreate(specUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Authority))
            throw new ArgumentException("API spec URL must be an absolute URI with a host.", nameof(specUrl));
    }
}
