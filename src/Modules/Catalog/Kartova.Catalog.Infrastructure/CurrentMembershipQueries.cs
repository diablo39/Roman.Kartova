using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Shared query over a component's current System membership — the <c>PartOf</c> edge(s), if
/// any, whose source is the given <see cref="EntityKind"/>/id (today at most one, enforced by
/// <c>ux_relationships_one_system</c>). Centralizes the predicate
/// <c>r.Type == RelationshipType.PartOf &amp;&amp; r.Source.Kind == sourceKind &amp;&amp; r.Source.Id == sourceId</c>
/// so <see cref="CatalogEndpointDelegates.CreateRelationshipAsync"/>'s pre-check, its
/// 23505-conflict re-query, <see cref="SetComponentSystemHandler"/>'s tracked-entity fetch, and
/// <see cref="CatalogEndpointDelegates.SetComponentSystemAsync"/>'s per-edge authorization
/// projection (ids only, via <c>.Select(r => r.Target.Id)</c> over <see cref="CurrentMembershipOf"/>)
/// cannot drift from one another.
/// </summary>
internal static class CurrentMembershipQueries
{
    /// <summary>
    /// Tracked <see cref="IQueryable{T}"/> over the component's current <c>PartOf</c> edge(s).
    /// Callers that need to remove/audit the entities (e.g. <see cref="SetComponentSystemHandler"/>)
    /// call <c>.ToListAsync</c> directly on this — no <c>.AsNoTracking()</c> is applied here, since
    /// EF Core tracking is on by default and that handler relies on it.
    /// </summary>
    public static IQueryable<Relationship> CurrentMembershipOf(
        CatalogDbContext db, EntityKind sourceKind, Guid sourceId)
        => db.Relationships.Where(r =>
            r.Type == RelationshipType.PartOf
            && r.Source.Kind == sourceKind
            && r.Source.Id == sourceId);

    /// <summary>
    /// Projects the component's current membership down to the occupying System's id, or
    /// <see langword="null"/> when the component has none. Used by the POST /relationships
    /// <c>PartOf</c> pre-check and its 23505-conflict re-query in
    /// <see cref="CatalogEndpointDelegates.CreateRelationshipAsync"/> — a nullable projection,
    /// not a <see cref="Guid.Empty"/> sentinel, since <see cref="Guid.Empty"/> is a
    /// legal-if-absurd value and conflating it with "no membership" would be a mutation blind spot.
    /// </summary>
    public static Task<Guid?> FindCurrentSystemIdAsync(
        CatalogDbContext db, EntityKind sourceKind, Guid sourceId, CancellationToken ct)
        => CurrentMembershipOf(db, sourceKind, sourceId)
            .Select(r => (Guid?)r.Target.Id)
            .FirstOrDefaultAsync(ct);
}
