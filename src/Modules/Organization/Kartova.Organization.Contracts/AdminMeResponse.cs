using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>ADR-0118: the signed-in platform operator, as seen by <c>GET /api/v1/admin/session/me</c>.</summary>
[ExcludeFromCodeCoverage]
public sealed record AdminMeResponse(Guid UserId, string Email, string DisplayName);
