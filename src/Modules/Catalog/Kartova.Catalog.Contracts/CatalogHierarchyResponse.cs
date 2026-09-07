using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Contracts;

/// <summary>The whole tenant as an Org → Team → { System, Ungrouped } → Component tree (E-03.F-03.S-02).
/// Team names are NOT included — resolved frontend-side from the teams list, as every Catalog surface does
/// (keeps this endpoint Catalog-local; ADR-0082). A bounded whole-org read (node cap + <see cref="Truncated"/>),
/// not a cursor list — ADR-0095 carve-out. <see cref="HierarchyTeamDto.TeamId"/> is present only for teams that
/// steward a system or own an ungrouped component; empty teams are injected frontend-side from the teams list.</summary>
[BoundedListResult(
    "Whole-org hierarchy tree, bounded by org size and capped at the node cap with Truncated; a tree, not a paged list — ADR-0095 carve-out.")]
[ExcludeFromCodeCoverage]
public sealed record CatalogHierarchyResponse(
    int TotalComponentCount,
    bool Truncated,
    IReadOnlyList<HierarchyTeamDto> Teams);

/// <summary>One steward team's subtree: its stewarded systems plus the ungrouped bucket of its own
/// system-less components. <see cref="ComponentCount"/> is the team's total descendant components.</summary>
[ExcludeFromCodeCoverage]
public sealed record HierarchyTeamDto(
    Guid TeamId,
    int ComponentCount,
    IReadOnlyList<HierarchySystemDto> Systems,
    HierarchyBucketDto Ungrouped);

/// <summary>A system node under its steward team, holding its member components regardless of their owning team.</summary>
[ExcludeFromCodeCoverage]
public sealed record HierarchySystemDto(
    Guid SystemId,
    string DisplayName,
    int ComponentCount,
    IReadOnlyList<HierarchyMemberDto> Members);

/// <summary>The "Ungrouped" node under a team: that team's components with no system membership.</summary>
[ExcludeFromCodeCoverage]
public sealed record HierarchyBucketDto(
    int ComponentCount,
    IReadOnlyList<HierarchyMemberDto> Members);

/// <summary>A leaf component. <see cref="Kind"/> is "application" or "service" (wire camelCase, ADR-0109).</summary>
[ExcludeFromCodeCoverage]
public sealed record HierarchyMemberDto(
    string Kind,
    Guid Id,
    string DisplayName);
