using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain;

[ExcludeFromCodeCoverage]
public sealed class User : ITenantOwned
{
    public Guid Id { get; set; }                       // = KeyCloak `sub`
    public TenantId TenantId { get; set; }
    public string Email { get; set; } = "";
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string DisplayName { get; set; } = "";     // denormalized: "given_name family_name" || email
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static string ComputeDisplayName(string? given, string? family, string email)
    {
        var full = $"{given?.Trim()} {family?.Trim()}".Trim();
        return string.IsNullOrWhiteSpace(full) ? email : full;
    }
}
