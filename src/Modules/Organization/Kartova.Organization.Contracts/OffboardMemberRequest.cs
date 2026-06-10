using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Body for <c>DELETE /api/v1/organizations/users/{id}</c> (slice-10 Task 6). The
/// <see cref="SuccessorUserId"/> receives the offboarded member's owned catalog
/// applications before the member is removed.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record OffboardMemberRequest(Guid SuccessorUserId);
