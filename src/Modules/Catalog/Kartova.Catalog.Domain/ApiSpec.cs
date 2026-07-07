using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

/// <summary>The current stored spec document (OpenAPI/AsyncAPI) for one <see cref="Api"/>
/// (ADR-0112). 1:1 with the owning API (unique <c>api_id</c>); versions deferred to E-21.
/// Content is opaque text — not parsed or validated for schema correctness this slice.</summary>
public sealed class ApiSpec : ITenantOwned
{
    public const int MaxContentBytes = 5 * 1024 * 1024;   // 5 MiB hard cap

    private Guid _id;

    public Guid Id => _id;
    public ApiId ApiId { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string MediaType { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Xmin { get; private set; }

    private ApiSpec() { }   // EF

    public static ApiSpec Create(
        ApiId apiId, TenantId tenantId, string content, string mediaType,
        Guid createdByUserId, DateTimeOffset createdAt)
    {
        Validate(content, mediaType);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));
        return new ApiSpec
        {
            _id = Guid.NewGuid(),
            ApiId = apiId,
            TenantId = tenantId,
            Content = content,
            MediaType = mediaType,
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt,
        };
    }

    /// <summary>Replaces the stored document in place (upsert path). Provenance stays the
    /// original <see cref="CreatedByUserId"/>/<see cref="CreatedAt"/> — one current spec, no history.</summary>
    public void Replace(string content, string mediaType, Guid updatedByUserId, DateTimeOffset updatedAt)
    {
        Validate(content, mediaType);
        if (updatedByUserId == Guid.Empty)
            throw new ArgumentException("updatedByUserId is required.", nameof(updatedByUserId));
        Content = content;
        MediaType = mediaType;
    }

    private static void Validate(string content, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("API spec content must not be empty.", nameof(content));
        if (System.Text.Encoding.UTF8.GetByteCount(content) > MaxContentBytes)
            throw new ArgumentException($"API spec content must be <= {MaxContentBytes} bytes.", nameof(content));
        if (!ApiMediaType.IsAllowed(mediaType))
            throw new ArgumentException("API spec media type must be application/json or application/yaml.", nameof(mediaType));
    }
}
