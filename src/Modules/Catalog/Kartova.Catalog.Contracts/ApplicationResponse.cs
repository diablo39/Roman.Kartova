using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel;

namespace Kartova.Catalog.Contracts;

/// <summary>
/// API response shape for a single Catalog application.
/// <para>
/// Slice 9 / E1 (ADR-0098): the optional <see cref="Owner"/> projection is enriched
/// by the list + detail handlers via <c>IUserDirectory</c> (cross-module port
/// implemented in the Organization module). Write-path handlers (register, edit,
/// deprecate, decommission, reactivate, undecommission, assign-team) currently
/// return a response with <see cref="Owner"/> = null — they materialize via the
/// no-argument <c>ToResponse()</c> extension, which omits the enrichment to keep
/// the write path free of an extra lookup. Consumers must treat the field as
/// optional.
/// </para>
/// <para>
/// Owner is declared as an init-only property after the positional list (not as
/// a positional parameter with a default) so the constructor arity remains stable
/// and existing positional call sites compile unchanged. Callers that need the
/// enrichment populate it with the record <c>with { Owner = ... }</c> idiom.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record ApplicationResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    Guid OwnerUserId,
    DateTimeOffset CreatedAt,
    Lifecycle Lifecycle,
    DateTimeOffset? SunsetDate,
    Guid? TeamId,
    string Version)
{
    public UserDisplayInfo? Owner { get; init; }
}
