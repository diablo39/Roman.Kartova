namespace Kartova.Catalog.Application;

/// <summary>
/// Full-update edit on an existing VM-kind <see cref="Kartova.Catalog.Domain.InfrastructureResource"/>
/// (ADR-0111 amendment, slice 2a Task 5). Team is IMMUTABLE on edit — there is no
/// team-move field here (mirrors <see cref="EditApplicationCommand"/>, which also edits
/// metadata only). <see cref="Attributes"/> is the already-validated
/// (<see cref="VmAttributes.Validate"/>) app-layer representation of the opaque jsonb
/// payload. <see cref="ExpectedVersion"/> drives optimistic concurrency — the handler sets
/// it as EF's <c>Xmin</c> OriginalValue so <c>SaveChanges</c> raises
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> on a stale ETag.
/// </summary>
public sealed record EditVmCommand(
    Kartova.Catalog.Domain.InfrastructureId Id,
    string DisplayName,
    string Description,
    string? Provider,
    VmAttributes Attributes,
    uint ExpectedVersion);
