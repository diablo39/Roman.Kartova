using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>
/// Metadata edit of an Environment (A2, design §"Domain"). <c>Type</c> is deliberately
/// NOT a field here — it is immutable on edit (mirrors <see cref="InfrastructureResource.Edit"/>'s
/// immutable <c>TeamId</c>). <see cref="ExpectedVersion"/> drives optimistic concurrency —
/// the handler sets it as EF's <c>Xmin</c> OriginalValue so <c>SaveChanges</c> raises
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> on a stale ETag.
/// </summary>
public sealed record EditEnvironmentCommand(
    EnvironmentId Id,
    string DisplayName,
    string Description,
    string? Region,
    string ResourceDetailsJson,
    uint ExpectedVersion);
