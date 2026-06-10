namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Cross-module port: reassigns ownership of all applications owned by <paramref name="fromUserId"/>
/// to <paramref name="toUserId"/> within the current tenant scope. Implemented by the Catalog module.
/// Consumed by the Organization module's <c>OffboardMemberHandler</c> so the offboarded member's
/// catalog applications are transferred to a chosen successor before the local <c>users</c>
/// projection row and KeyCloak identity are deleted (slice-10 Task 6). Mirrors the slice-8
/// <see cref="IApplicationCountByTeamReader"/> port — interface in SharedKernel, implementation in
/// Catalog.Infrastructure, consumed by Organization, with no direct cross-module project reference.
/// </summary>
public interface IApplicationOwnerReassigner
{
    /// <summary>
    /// Reassigns every application owned by <paramref name="fromUserId"/> in the active tenant
    /// scope to <paramref name="toUserId"/>. Returns the number of applications reassigned.
    /// </summary>
    Task<int> ReassignOwnerAsync(Guid fromUserId, Guid toUserId, CancellationToken ct);
}
