using Kartova.SharedKernel;

namespace Kartova.SharedKernel.Identity;

/// <summary>
/// Cross-module read port over the local <c>users</c> projection (per-tenant
/// shadow of KeyCloak realm users). Implemented by
/// <c>OrganizationUserDirectory</c> in the Organization module and consumed by
/// other modules (e.g. Catalog) that need to enrich a query response with a
/// user's display name / email without taking a direct dependency on
/// Organization's DbContext. Slice-9 spec §6.6 + ADR-0098.
///
/// <para>
/// All lookups run inside the caller's active <c>ITenantScope</c>, so they are
/// subject to PostgreSQL Row-Level Security on the <c>users</c> table: a user
/// belonging to another tenant is treated as if they did not exist (RLS hides
/// the row from the projection's <c>SELECT</c>). Callers must NOT interpret a
/// null / missing return as "user has been deleted" — it may equally mean
/// "user exists but is invisible from this tenant scope".
/// </para>
///
/// <para>
/// The contract returns plain data carriers (<see cref="UserDisplayInfo"/>) so
/// modules can transport across the boundary without leaking Organization's
/// EF entity shape (<c>User</c>) — see ADR-0098 §6.6 for the rationale.
/// </para>
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Resolves a single user's display info by id.
    /// </summary>
    /// <param name="userId">KeyCloak user id (mirrored as the primary key in
    /// the local <c>users</c> projection).</param>
    /// <param name="ct">Cancellation token tied to the request lifetime.</param>
    /// <returns>
    /// The user's display info when a matching row exists in the current
    /// tenant scope; <c>null</c> when the user is unknown OR when the row
    /// exists but is hidden by RLS. Callers that need a guaranteed display
    /// string should fall back to an empty string (or an "Unknown user"
    /// label) on null — consistent with the team-detail page's behavior
    /// for missing owners.
    /// </returns>
    Task<UserDisplayInfo?> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Batch-resolves multiple users in a single round trip.
    /// </summary>
    /// <param name="userIds">Set of KeyCloak user ids to look up. Passing an
    /// empty collection short-circuits to an empty dictionary without
    /// touching the database.</param>
    /// <param name="ct">Cancellation token tied to the request lifetime.</param>
    /// <returns>
    /// A dictionary keyed by user id containing only the users that resolved
    /// successfully (matched a row visible under RLS). Ids that were not
    /// found are simply absent from the dictionary — callers MUST NOT
    /// expect a sentinel null entry per missing id. The recommended call
    /// site pattern is <c>dict.TryGetValue(id, out var info)</c> followed by
    /// a default of <c>string.Empty</c> for both DisplayName and Email when
    /// false (matches how the Catalog applications enricher renders missing
    /// owners).
    /// </returns>
    Task<IReadOnlyDictionary<Guid, UserDisplayInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
