using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel;

namespace Kartova.Catalog.Contracts;

/// <summary>
/// API response shape for a single Catalog application.
/// <para>
/// ADR-0103: <see cref="CreatedByUserId"/> is immutable creation provenance (the
/// individual who registered the app); <see cref="TeamId"/> is the required owning
/// team. The optional <see cref="CreatedBy"/> projection is enriched by the list +
/// detail handlers via <c>IUserDirectory</c> (cross-module port implemented in the
/// Organization module). Write-path handlers (register, edit, deprecate,
/// decommission, reactivate, undecommission, assign-team) return a response with
/// <see cref="CreatedBy"/> = null — they materialize via the no-argument
/// <c>ToResponse()</c> extension, which omits the enrichment to keep the write path
/// free of an extra lookup. Consumers must treat the field as optional (and a
/// once-resolving id may stop resolving after the creator is offboarded — render
/// "former member").
/// </para>
/// <para>
/// CreatedBy is declared as an init-only property after the positional list (not as
/// a positional parameter with a default) so the constructor arity remains stable
/// and existing positional call sites compile unchanged. Callers that need the
/// enrichment populate it with the record <c>with { CreatedBy = ... }</c> idiom.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record ApplicationResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    Lifecycle Lifecycle,
    DateTimeOffset? SunsetDate,
    Guid TeamId,
    string Version)
{
    public UserDisplayInfo? CreatedBy { get; init; }

    // ADR-0110 §5.3: successor reference set on deprecate. Init-only after the
    // positional list for the same reason as CreatedBy — constructor arity stays
    // stable for existing positional call sites. SuccessorDisplayName mirrors the
    // CreatedBy enrichment pattern: null on the write path (deprecate), populated
    // only by detail-read handlers (task C5).
    public Guid? SuccessorApplicationId { get; init; }
    public string? SuccessorDisplayName { get; init; }
}
