using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Pure Org → Team → { System, Ungrouped } → Component assembler (E-03.F-03.S-02). No I/O.
/// A component with a <c>PartOf</c> membership lands under its system, which sits under the system's
/// STEWARD team — regardless of the component's own owning team (ADR-0111 amendment); a component
/// without a membership lands in its OWNING team's Ungrouped bucket. Every component appears exactly
/// once. Iteration order is deterministic (teams by id, systems/members by displayName then id) so
/// truncation at <paramref name="nodeCap"/> is stable. Mirrors the pure-helper precedent
/// <see cref="ImpactAnalysis"/> / <see cref="DerivedDependencies"/>.</summary>
public static class HierarchyAssembler
{
    public sealed record SystemRow(Guid Id, string DisplayName, Guid StewardTeamId);

    public sealed record ComponentRow(EntityKind Kind, Guid Id, string DisplayName, Guid OwningTeamId);

    public static CatalogHierarchyResponse Build(
        IReadOnlyList<SystemRow> systems,
        IReadOnlyList<ComponentRow> components,
        IReadOnlyDictionary<(EntityKind Kind, Guid Id), Guid> partOf,
        int nodeCap)
    {
        var systemById = systems.ToDictionary(s => s.Id);

        // Route each component to a bucket key: ("sys", systemId) if it has a membership whose system
        // still exists, else ("team", owningTeamId). Deterministic component order for stable truncation.
        var ordered = components
            .OrderBy(c => c.DisplayName, StringComparer.Ordinal)
            .ThenBy(c => c.Id);

        var systemMembers = new Dictionary<Guid, List<HierarchyMemberDto>>();
        var ungroupedByTeam = new Dictionary<Guid, List<HierarchyMemberDto>>();
        var total = 0;
        var truncated = false;

        foreach (var c in ordered)
        {
            if (total >= nodeCap) { truncated = true; break; }
            var member = new HierarchyMemberDto(KindWire(c.Kind), c.Id, c.DisplayName);

            if (partOf.TryGetValue((c.Kind, c.Id), out var sysId) && systemById.ContainsKey(sysId))
                Append(systemMembers, sysId, member);
            else
                Append(ungroupedByTeam, c.OwningTeamId, member);

            total++;
        }

        // Teams present = steward teams of any system ∪ owning teams with ungrouped components.
        var teamIds = systems.Select(s => s.StewardTeamId)
            .Concat(ungroupedByTeam.Keys)
            .Distinct()
            .OrderBy(id => id);

        var teams = new List<HierarchyTeamDto>();
        foreach (var teamId in teamIds)
        {
            var teamSystems = systems
                .Where(s => s.StewardTeamId == teamId)
                .OrderBy(s => s.DisplayName, StringComparer.Ordinal)
                .ThenBy(s => s.Id)
                .Select(s =>
                {
                    var members = Sorted(systemMembers, s.Id);
                    return new HierarchySystemDto(s.Id, s.DisplayName, members.Count, members);
                })
                .ToList();

            var ungroupedMembers = Sorted(ungroupedByTeam, teamId);
            var ungrouped = new HierarchyBucketDto(ungroupedMembers.Count, ungroupedMembers);
            var count = teamSystems.Sum(s => s.ComponentCount) + ungrouped.ComponentCount;
            teams.Add(new HierarchyTeamDto(teamId, count, teamSystems, ungrouped));
        }

        return new CatalogHierarchyResponse(total, truncated, teams);

        static void Append(Dictionary<Guid, List<HierarchyMemberDto>> map, Guid key, HierarchyMemberDto m)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add(m);
        }

        static IReadOnlyList<HierarchyMemberDto> Sorted(Dictionary<Guid, List<HierarchyMemberDto>> map, Guid key)
            => map.TryGetValue(key, out var list)
                ? list.OrderBy(m => m.DisplayName, StringComparer.Ordinal).ThenBy(m => m.Id).ToList()
                : [];

        static string KindWire(EntityKind k) => k switch
        {
            EntityKind.Application => "application",
            EntityKind.Service => "service",
            _ => throw new ArgumentOutOfRangeException(nameof(k), k, "hierarchy leaf must be application or service"),
        };
    }
}
