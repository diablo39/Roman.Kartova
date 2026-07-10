using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Contracts;

/// <summary>A single service's derived depends-on relationships, computed on read (ADR-0111 §Decision 5).
/// Bounded (one service's derived neighbours; small N) so it returns flat arrays, not <c>CursorPage&lt;T&gt;</c>
/// — ADR-0095 bounded-list carve-out.</summary>
[BoundedListResult(
    "A single service's derived depends-on set (dependencies + dependents) is bounded and small; no pagination — ADR-0095 carve-out.")]
[ExcludeFromCodeCoverage]
public sealed record DerivedDependenciesResponse(
    IReadOnlyList<DerivedDependencyItem> Dependencies,  // services THIS one derives a depends-on TO (source == focus)
    IReadOnlyList<DerivedDependencyItem> Dependents);   // services that derive a depends-on on THIS one (target == focus)
