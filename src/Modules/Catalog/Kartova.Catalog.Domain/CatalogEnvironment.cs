using System.Text.Json;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

/// <summary>
/// Deployment-environment catalog entity (E-02.F-05, ADR-0117). First-class,
/// tenant-owned, but NOT team-owned — environments are shared deployment targets,
/// so writes are role-gated only (no <see cref="ITeamScopedResource"/>). Named
/// <c>CatalogEnvironment</c> to avoid the <c>System.Environment</c> clash, mirroring
/// <see cref="CatalogSystem"/>. <c>ResourceDetails</c> is an opaque jsonb key/value
/// map (loose "resource details", validated as well-formed JSON object). The
/// Postgres <c>xmin</c> concurrency token maps to <see cref="Xmin"/>.
/// </summary>
public sealed class CatalogEnvironment : ITenantOwned
{
    private Guid _id;   // plain-Guid PK backing field (same pattern as Application/Infrastructure)

    public EnvironmentId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public EnvironmentType Type { get; private set; }
    public string? Region { get; private set; }
    public string ResourceDetails { get; private set; } = "{}";
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Xmin { get; private set; }

    private CatalogEnvironment() { }   // EF

    private CatalogEnvironment(
        EnvironmentId id, TenantId tenantId, string displayName, string description, EnvironmentType type,
        string? region, string resourceDetailsJson, Guid createdByUserId, DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        DisplayName = displayName;
        Description = description;
        Type = type;
        Region = region;
        ResourceDetails = resourceDetailsJson;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public static CatalogEnvironment Create(
        string displayName, string description, EnvironmentType type, string? region,
        string resourceDetailsJson, Guid createdByUserId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(displayName, description, type, region, resourceDetailsJson, createdByUserId, tenantId, clock.GetUtcNow());
    }

    /// <summary>Overload taking an explicit <paramref name="createdAt"/> — for seed/test fixtures.</summary>
    public static CatalogEnvironment Create(
        string displayName, string description, EnvironmentType type, string? region,
        string resourceDetailsJson, Guid createdByUserId, TenantId tenantId, DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (!Enum.IsDefined(type))
            throw new ArgumentException("Unknown environment type.", nameof(type));
        region = Normalize(region);
        ValidateOptional(region, nameof(region));
        ValidateResourceDetails(resourceDetailsJson);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));

        return new CatalogEnvironment(EnvironmentId.New(), tenantId, displayName, description, type, region, resourceDetailsJson, createdByUserId, createdAt);
    }

    /// <summary>
    /// Full-replacement edit (A2). No field is immutable — Environment has no owning
    /// team to protect (unlike <c>InfrastructureResource.Edit</c>, which keeps
    /// <c>TeamId</c> fixed). Same invariants as <see cref="Create"/>.
    /// </summary>
    public void Edit(string displayName, string description, EnvironmentType type, string? region, string resourceDetailsJson)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (!Enum.IsDefined(type))
            throw new ArgumentException("Unknown environment type.", nameof(type));
        region = Normalize(region);
        ValidateOptional(region, nameof(region));
        ValidateResourceDetails(resourceDetailsJson);

        DisplayName = displayName;
        Description = description;
        Type = type;
        Region = region;
        ResourceDetails = resourceDetailsJson;
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Environment display name must not be empty.", nameof(displayName));
        if (displayName.Length > 128)
            throw new ArgumentException("Environment display name must be <= 128 characters.", nameof(displayName));
    }

    private static void ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Environment description must not be empty.", nameof(description));
        if (description.Length > 4096)
            throw new ArgumentException("Environment description must be <= 4096 characters.", nameof(description));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateOptional(string? value, string name)
    {
        if (value is not null && value.Length > 256)
            throw new ArgumentException($"Environment {name} must be <= 256 characters.", name);
    }

    private static void ValidateResourceDetails(string resourceDetailsJson)
    {
        if (string.IsNullOrWhiteSpace(resourceDetailsJson))
            throw new ArgumentException("Environment resource details must not be empty (use '{}' for none).", nameof(resourceDetailsJson));
        try
        {
            using var doc = JsonDocument.Parse(resourceDetailsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Environment resource details must be a JSON object.", nameof(resourceDetailsJson));
        }
        catch (JsonException)
        {
            throw new ArgumentException("Environment resource details must be valid JSON.", nameof(resourceDetailsJson));
        }
    }
}
