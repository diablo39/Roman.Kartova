using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain;

public sealed partial class Team : ITenantOwned, ITeamOwnedResource
{
    private Guid _id;

    public TeamId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    Guid ITeamOwnedResource.TeamId => _id;

    private Team() { /* EF */ }

    public static Team Create(string displayName, string? description, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateDisplayName(displayName);
        var normalizedDescription = NormalizeDescription(description);
        ValidateDescription(normalizedDescription);
        return new Team
        {
            _id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = displayName,
            Description = normalizedDescription,
            CreatedAt = clock.GetUtcNow(),
        };
    }

    public void Rename(string newDisplayName, string? newDescription)
    {
        ValidateDisplayName(newDisplayName);
        var normalizedDescription = NormalizeDescription(newDescription);
        ValidateDescription(normalizedDescription);
        DisplayName = newDisplayName;
        Description = normalizedDescription;
    }

    private static void ValidateDisplayName(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Team display name must not be empty.", nameof(s));
        if (s.Length > 128)
            throw new ArgumentException("Team display name must be <= 128 characters.", nameof(s));
    }

    private static void ValidateDescription(string? s)
    {
        if (s is { Length: > 512 })
            throw new ArgumentException("Team description must be <= 512 characters.", nameof(s));
    }

    // Canonical normalization for Description: empty or whitespace-only strings
    // collapse to null so the DB sees a single representation regardless of which
    // creation path (SPA dialog, API consumer, test seed) populates the field.
    // Addresses deep-review SF-4.
    private static string? NormalizeDescription(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
