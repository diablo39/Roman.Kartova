using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>
/// List VM-kind Infrastructure resources visible to the current tenant (RLS-filtered). Type is
/// implicitly fixed to <see cref="Kartova.Catalog.Domain.InfrastructureType.VirtualMachine"/> by
/// the handler — there is no <c>Type</c> parameter here. ADR-0095.
/// <para>
/// <paramref name="TeamId"/> — ADR-0107 multi-select team filter, same semantics as
/// <see cref="ListInfrastructureQuery.TeamId"/>.
/// </para>
/// <para>
/// <paramref name="PowerState"/>/<paramref name="Os"/>/<paramref name="Region"/>/
/// <paramref name="Hostname"/> — single-value equality filters over the jsonb
/// <c>VmAttributes</c> payload, applied via <c>EF.Functions.JsonContains</c> (Postgres
/// <c>@&gt;</c> containment). <paramref name="IpAddress"/> is an array-containment check against
/// the <c>ipAddresses</c> jsonb array. Each is <c>null</c> when absent (no predicate); non-null
/// values are encoded into the cursor f-map under their own camelCase key so a mid-pagination
/// change trips <c>CursorFilterMismatchException</c>.
/// </para>
/// </summary>
public sealed record ListVmsQuery(
    VmSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    Guid[] TeamId,
    string? PowerState = null,
    string? Os = null,
    string? Region = null,
    string? Hostname = null,
    string? IpAddress = null);
