using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>
/// Request body for <c>PUT /api/v1/catalog/applications/{id}/team</c> (slice 8,
/// ADR-0098 §6). <c>TeamId</c> is nullable — <c>null</c> means "unassign the
/// application from its current team". A non-null value must reference an
/// existing team in the active tenant scope (handler validates via
/// <c>IOrganizationTeamExistenceChecker</c>; mismatch surfaces as 422).
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record AssignTeamRequest(Guid? TeamId);
