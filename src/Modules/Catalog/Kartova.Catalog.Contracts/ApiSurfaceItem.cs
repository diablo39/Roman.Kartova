using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

/// <summary>One API on a component's surface, with the metadata the panel renders.
/// For Consumes rows <see cref="Origin"/> is always <see cref="ApiSurfaceOrigin.Direct"/> and the
/// Via* fields are null. For Derived rows, <see cref="ViaApplicationId"/> is always set;
/// <see cref="ViaApplicationDisplayName"/> is best-effort and MAY be null even when <see cref="Origin"/> is
/// <see cref="ApiSurfaceOrigin.Derived"/>.
/// <para><see cref="RelationshipId"/> is the id of the underlying direct edge (so the UI can remove it);
/// it is non-null only for <see cref="ApiSurfaceOrigin.Direct"/> rows. Derived rows have no single owning
/// edge on this component and are not directly removable, so their <see cref="RelationshipId"/> is null.</para></summary>
[ExcludeFromCodeCoverage]
public sealed record ApiSurfaceItem(
    Guid ApiId,
    string DisplayName,
    ApiStyle Style,
    string Version,
    bool HasSpec,
    ApiSurfaceOrigin Origin,
    Guid? ViaApplicationId,
    string? ViaApplicationDisplayName,
    Guid? RelationshipId);
