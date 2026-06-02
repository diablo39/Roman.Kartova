using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Request body for <c>PUT /api/v1/organizations/me</c> — updates the current
/// tenant's <c>Organization</c> profile (slice-9 spec §4). Domain validation
/// (display-name length, description length, IANA timezone) is enforced by
/// <c>Organization.UpdateProfile</c> and surfaces via
/// <c>DomainValidationExceptionHandler</c> as RFC 7807 400.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record UpdateOrgProfileRequest(string DisplayName, string? Description, string DefaultTimeZone);
