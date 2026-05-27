using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Response shape for <c>GET /api/v1/organizations/me</c> — surfaces the
/// current tenant's <c>Organization</c> profile (slice-9 spec §4). Replaces
/// the older <see cref="OrganizationDto"/> on the /me read surface;
/// <c>OrganizationDto</c> is retained for the platform-admin tenant-creation
/// endpoint (<c>POST /api/v1/admin/organizations</c>) where the response shape
/// is intentionally narrower.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record OrgProfileResponse(
    Guid Id,
    string DisplayName,
    string? Description,
    string DefaultTimeZone,
    string? LogoEtag,
    string? LogoMimeType,
    DateTimeOffset CreatedAt);
