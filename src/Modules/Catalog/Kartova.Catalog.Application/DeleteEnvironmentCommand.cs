using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>
/// Hard-delete command for an Environment (A2). Mirrors <see cref="DeleteVmCommand"/>:
/// <see cref="ExpectedVersion"/> is the decoded <c>If-Match</c> version, set as the EF
/// <c>OriginalValue(Xmin)</c> so the generated <c>DELETE ... WHERE xmin = :expected</c>
/// raises <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> on a
/// stale caller-supplied version (mapped to 412 upstream). No lifecycle/soft-delete and
/// no relationship edges reference Environment yet (deployment events land in a later
/// sub-slice, ADR-0117) — a plain row removal.
/// </summary>
public sealed record DeleteEnvironmentCommand(EnvironmentId Id, uint ExpectedVersion);
