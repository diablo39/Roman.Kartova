using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// A component's System, flattened for list-row enrichment. <c>public</c>, not <c>internal</c>: it
/// is the return-type payload of <see cref="ISystemMembershipEnricher"/>, which is itself public
/// because it appears on <see cref="ListApplicationsHandler"/>'s public constructor (CS0051).
/// </summary>
public readonly record struct SystemRef(Guid Id, string DisplayName);

/// <summary>
/// Shared query over a component's current System membership — the <c>PartOf</c> edge(s), if
/// any, whose source is the given <see cref="EntityKind"/>/id (today at most one, enforced by
/// <c>ux_relationships_one_system</c>). Centralizes the predicate
/// <c>r.Type == RelationshipType.PartOf &amp;&amp; r.Source.Kind == sourceKind &amp;&amp; r.Source.Id == sourceId</c>
/// so <see cref="CatalogEndpointDelegates.CreateRelationshipAsync"/>'s pre-check, its
/// 23505-conflict re-query, <see cref="SetComponentSystemHandler"/>'s tracked-entity fetch,
/// <see cref="CatalogEndpointDelegates.SetComponentSystemAsync"/>'s per-edge authorization
/// projection (ids only, via <c>.Select(r => r.Target.Id)</c> over <see cref="CurrentMembershipOf"/>),
/// and <see cref="SystemsForComponentsAsync"/>'s page-batched list-column enrichment (set-valued
/// source overload) cannot drift from one another.
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

    /// <summary>Multi-component overload of <see cref="CurrentMembershipOf(CatalogDbContext, EntityKind, Guid)"/>
    /// for page-batched reads. Same predicate, set-valued source.</summary>
    public static IQueryable<Relationship> CurrentMembershipOf(
        CatalogDbContext db, EntityKind sourceKind, IReadOnlyCollection<Guid> sourceIds)
        => db.Relationships.Where(r =>
            r.Type == RelationshipType.PartOf
            && r.Source.Kind == sourceKind
            && sourceIds.Contains(r.Source.Id));

    /// <summary>
    /// Resolves the System (id + display name) for a whole page of components — the list-column
    /// counterpart to <see cref="FindCurrentSystemIdAsync"/>'s single lookup. Batched PER PAGE
    /// (never per row): two round trips — edges, then System names — joined client-side.
    /// <para>
    /// Why not one server-side <c>.Join()</c>: Npgsql will not translate a value-object /
    /// complex-property member used as a Join KEY selector. <c>Relationship.Target</c> is a
    /// ComplexProperty, so <c>r => r.Target.Id</c> as an outer key selector fails the same way
    /// <c>m.TeamId.Value</c> did in <c>UserQueries.GetDetailAsync</c> — which shipped as a 500 on
    /// <c>GET /users/{id}</c> before it was caught. Two round trips is the established fix in
    /// this codebase (see that method's comment). Complex-property members remain fine in
    /// <c>Where</c> and <c>Select</c>, which is why the first query below works.
    /// </para>
    /// Components with no <c>PartOf</c> edge are simply absent from the dictionary (callers render
    /// "unassigned"). Empty input short-circuits without a query. Both tables are RLS-scoped and
    /// both queries run on the request's single connection + transaction (ADR-0090), so the
    /// second round trip inherits the same <c>SET LOCAL app.current_tenant_id</c> — a System from
    /// another tenant resolves to no name rather than leaking one.
    /// </summary>
    public static async Task<Dictionary<Guid, SystemRef>> SystemsForComponentsAsync(
        CatalogDbContext db,
        EntityKind sourceKind,
        IReadOnlyCollection<Guid> componentIds,
        CancellationToken ct)
    {
        if (componentIds.Count == 0)
            return new Dictionary<Guid, SystemRef>();

        var edges = await CurrentMembershipOf(db, sourceKind, componentIds)
            .Select(r => new { ComponentId = r.Source.Id, SystemId = r.Target.Id })
            .ToListAsync(ct);
        if (edges.Count == 0)
            return new Dictionary<Guid, SystemRef>();

        var systemIds = edges.Select(e => e.SystemId).ToHashSet();
        var names = await db.Systems
            .Where(s => systemIds.Contains(EF.Property<Guid>(s, EfSystemConfiguration.IdFieldName)))
            .Select(s => new
            {
                Id = EF.Property<Guid>(s, EfSystemConfiguration.IdFieldName),
                s.DisplayName,
            })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        // DistinctBy, not a bare ToDictionary: `ux_relationships_one_system` makes a second
        // PartOf edge impossible and the migration that created it collapsed pre-existing
        // duplicates in the same transaction, so a duplicate key cannot occur today. One call
        // is still cheaper than the alternative failure mode — a duplicate-key ArgumentException
        // here would 500 the entire list endpoint, not just the offending row.
        // An edge whose System is invisible (dangling target / another tenant) is dropped, so the
        // row renders unassigned rather than half-populated.
        return edges
            .Where(e => names.ContainsKey(e.SystemId))
            .DistinctBy(e => e.ComponentId)
            .ToDictionary(e => e.ComponentId, e => new SystemRef(e.SystemId, names[e.SystemId]));
    }
}
