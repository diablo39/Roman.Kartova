using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>
/// Full-update edit of a Environment (A2). Mirrors <see cref="EditVmCommand"/>: no
/// field is immutable (Environment has no owning team to protect). <see cref="ExpectedVersion"/>
/// drives optimistic concurrency — the handler sets it as EF's <c>Xmin</c> OriginalValue so
/// <c>SaveChanges</c> raises <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
/// on a stale ETag.
/// </summary>
public sealed record EditEnvironmentCommand(
    EnvironmentId Id,
    string DisplayName,
    string Description,
    EnvironmentType Type,
    string? Region,
    string ResourceDetailsJson,
    uint ExpectedVersion);
