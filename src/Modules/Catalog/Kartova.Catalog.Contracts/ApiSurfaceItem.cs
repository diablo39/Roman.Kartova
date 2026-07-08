using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

/// <summary>One API on a component's surface, with the metadata the panel renders.
/// For Consumes rows <see cref="Origin"/> is always <see cref="ApiSurfaceOrigin.Direct"/> and the
/// Via* fields are null. For Derived rows, <see cref="ViaApplicationId"/> is always set;
/// <see cref="ViaApplicationDisplayName"/> is best-effort and MAY be null even when <see cref="Origin"/> is
/// <see cref="ApiSurfaceOrigin.Derived"/>.</summary>
[ExcludeFromCodeCoverage]
public sealed record ApiSurfaceItem(
    Guid ApiId,
    string DisplayName,
    ApiStyle Style,
    string Version,
    bool HasSpec,
    ApiSurfaceOrigin Origin,
    Guid? ViaApplicationId,
    string? ViaApplicationDisplayName);
