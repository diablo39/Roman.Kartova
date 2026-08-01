namespace Kartova.Catalog.Application;

/// <summary>One row of a component's current System membership: the edge id and the System it points at.</summary>
public readonly record struct ExistingMembership(Guid RelationshipId, Guid SystemId);

/// <summary>What a membership write must do: which <c>PartOf</c> edges to delete, and whether to insert the requested one.</summary>
public sealed record SystemMembershipDecision(IReadOnlyList<Guid> RelationshipIdsToDelete, bool InsertRequested);

/// <summary>
/// Pure at-most-one-System decision (ADR-0111 amended 2026-07-30). A component may be
/// <c>PartOf</c> at most one System, so writing a membership replaces any existing one.
/// Kept database-free — the handler executes the decision, this class makes it.
/// </summary>
public static class SystemMembership
{
    public static SystemMembershipDecision Decide(
        IReadOnlyList<ExistingMembership> existing, Guid? requestedSystemId)
    {
        if (requestedSystemId is not { } requested)
            return new SystemMembershipDecision([.. existing.Select(e => e.RelationshipId)], false);

        var keep = existing
            .Where(e => e.SystemId == requested)
            .Select(e => (Guid?)e.RelationshipId)
            .FirstOrDefault();

        var delete = existing
            .Where(e => e.RelationshipId != keep)
            .Select(e => e.RelationshipId)
            .ToList();

        return new SystemMembershipDecision(delete, keep is null);
    }
}
