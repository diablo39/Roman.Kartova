namespace Kartova.Catalog.Application;

/// <summary>
/// Hard-delete command for a VM-kind Infrastructure resource (ADR-0111 amendment, slice 2a
/// Task 6). <c>ExpectedVersion</c> is the decoded <c>If-Match</c> version — the handler sets it
/// as the EF <c>OriginalValue(Xmin)</c> so the generated <c>DELETE ... WHERE xmin = :expected</c>
/// raises <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> on a stale
/// caller-supplied version (mapped to 412 upstream). There is no lifecycle/soft-delete for
/// Infrastructure in slice 2a and no relationship edges exist yet, so this is a plain row
/// removal — unlike Application's Deprecate/Decommission transitions.
/// </summary>
public sealed record DeleteVmCommand(Kartova.Catalog.Domain.InfrastructureId Id, uint ExpectedVersion);
