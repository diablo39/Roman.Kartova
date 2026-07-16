using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel;

namespace Kartova.Catalog.Contracts;

/// <summary><see cref="CreatedBy"/> is enriched by the list read handler via
/// <c>IUserDirectory</c> (mirrors <c>ServiceResponse</c>/<c>ApplicationResponse</c>);
/// it is null when the creator is a system actor or no longer resolves in the
/// current tenant scope (attribution kept as history, ADR-0102).</summary>
[ExcludeFromCodeCoverage]
public sealed record RelationshipResponse(
    Guid Id, EntityRefDto Source, EntityRefDto Target,
    RelationshipType Type, RelationshipOrigin Origin,
    Guid CreatedByUserId, DateTimeOffset CreatedAt)
{
    public UserDisplayInfo? CreatedBy { get; init; }
}
