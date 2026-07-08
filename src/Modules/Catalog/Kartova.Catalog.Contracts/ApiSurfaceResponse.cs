using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Contracts;

/// <summary>A component's API surface. Bounded (a single component's direct+derived APIs; small N)
/// so it returns flat arrays, not <c>CursorPage&lt;T&gt;</c> — ADR-0095 bounded-list carve-out.</summary>
[BoundedListResult(
    "A single component's API surface (direct + derived edges) is bounded and small; no pagination — ADR-0095 carve-out.")]
[ExcludeFromCodeCoverage]
public sealed record ApiSurfaceResponse(
    IReadOnlyList<ApiSurfaceItem> Provides,
    IReadOnlyList<ApiSurfaceItem> Consumes);
