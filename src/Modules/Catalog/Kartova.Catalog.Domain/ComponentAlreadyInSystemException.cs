namespace Kartova.Catalog.Domain;

/// <summary>
/// Thrown when the System-membership write handler's SaveChangesAsync loses a concurrent
/// race: two writers both passed the application-level pre-check and named
/// different Systems for the same component, but the database's <c>ux_relationships_one_system</c>
/// partial unique index (ADR-0111 amended 2026-07-30) allows only one to commit. The losing
/// request's Postgres 23505 unique-violation is translated to this typed exception in the
/// handler, then to <c>409 ProblemTypes.ComponentAlreadyInSystem</c> by the endpoint delegate.
/// </summary>
public sealed class ComponentAlreadyInSystemException(EntityRef component)
    : InvalidOperationException(
        $"{component.Kind} '{component.Id}' already has a System membership assigned by a concurrent writer.")
{
    public EntityRef Component { get; } = component;
}
