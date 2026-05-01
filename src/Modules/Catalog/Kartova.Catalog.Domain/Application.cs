using System.Text.RegularExpressions;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

public sealed class Application : ITenantOwned
{
    public ApplicationId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Application(
        ApplicationId id,
        TenantId tenantId,
        string name,
        string displayName,
        string description,
        Guid ownerUserId,
        DateTimeOffset createdAt)
    {
        Id = id;
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
            DateTimeOffset.UtcNow);
    }

    // Kebab-case: starts with a lowercase ASCII letter, then lowercase letters/digits/dashes,
    // dashes only between alphanumeric segments, no leading/trailing/double dash. Matches the
    // SPA's zod rule (registerApplicationSchema) so server-side validation is the source of truth
    // and the SPA check is purely UX feedback. Spec §5.3, E-02.F-01.S-07.
    private static readonly Regex KebabCase =
        new("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.Compiled);

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
        if (!KebabCase.IsMatch(name))
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
