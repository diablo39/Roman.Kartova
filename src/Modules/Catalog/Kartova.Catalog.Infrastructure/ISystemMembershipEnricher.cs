using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Port over <see cref="CurrentMembershipQueries.SystemsForComponentsAsync"/> — the page-batched
/// System-column lookup shared by <see cref="ListApplicationsHandler"/> (and, from Task 4,
/// <c>ListServicesHandler</c>). Exists purely for testability, not layering: the EF Core InMemory
/// provider (used throughout this module's handler unit tests) cannot translate a query that
/// materializes <see cref="Kartova.Catalog.Domain.Relationship"/> — its <c>Source</c>/<c>Target</c>
/// members are <c>ComplexProperty</c>-mapped with a <c>HasConversion&lt;string&gt;()</c> on
/// <c>Kind</c>, and the InMemory provider's shaper throws <c>KeyNotFoundException</c> compiling
/// any query — <c>Where</c>, <c>Select</c>, or bare materialization — that touches it (Npgsql
/// translates the identical query fine, proven by <c>SystemEnrichmentTranslationTests</c>). Handler
/// unit tests that don't care about System enrichment stub this port with an empty-dictionary
/// response via NSubstitute, exactly like the existing <see cref="Kartova.SharedKernel.Identity.IUserDirectory"/>
/// stub, so they never execute a Relationships query at all. Production DI wires the real
/// implementation below, which simply forwards to the static query.
/// </summary>
/// <remarks>
/// <c>public</c>, not <c>internal</c>: it appears as a parameter on <see cref="ListApplicationsHandler"/>'s
/// public constructor, and CS0051 forbids a less-accessible parameter type there. <see cref="SystemRef"/>
/// is likewise made <see langword="public"/> for the same reason (it appears in this interface's
/// return type) — the DTO itself still only carries the two fields list rows need to render the
/// column.
/// </remarks>
public interface ISystemMembershipEnricher
{
    Task<Dictionary<Guid, SystemRef>> SystemsForComponentsAsync(
        CatalogDbContext db,
        EntityKind sourceKind,
        IReadOnlyCollection<Guid> componentIds,
        CancellationToken ct);
}

/// <summary>Production implementation — a thin forward to <see cref="CurrentMembershipQueries"/>.</summary>
internal sealed class SystemMembershipEnricher : ISystemMembershipEnricher
{
    public Task<Dictionary<Guid, SystemRef>> SystemsForComponentsAsync(
        CatalogDbContext db,
        EntityKind sourceKind,
        IReadOnlyCollection<Guid> componentIds,
        CancellationToken ct)
        => CurrentMembershipQueries.SystemsForComponentsAsync(db, sourceKind, componentIds, ct);
}
