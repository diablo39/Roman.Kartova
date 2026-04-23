using Kartova.SharedKernel.Multitenancy;
using Wolverine;

namespace Kartova.SharedKernel.Wolverine;

/// <summary>
/// Wolverine middleware that mirrors TenantScopeEndpointFilter for message handlers.
/// Populates ITenantContext from the message envelope's "tenant_id" header, begins the scope,
/// commits after handler success, rolls back on exception. See ADR-0090.
/// </summary>
public static class TenantScopeWolverineMiddleware
{
    public const string TenantIdHeader = "tenant_id";

    public static async Task<IAsyncTenantScopeHandle?> BeforeAsync(
        Envelope envelope,
        ITenantContext tenantContext,
        ITenantScope scope,
        CancellationToken ct)
    {
        if (!envelope.Headers.TryGetValue(TenantIdHeader, out var raw) ||
            raw is null ||
            !Multitenancy.TenantId.TryParse(raw, out var id))
        {
            // No tenant header → treat as non-tenant-scoped (e.g. platform-admin messages).
            return null;
        }

        tenantContext.Populate(id, Array.Empty<string>());
        return await scope.BeginAsync(id, ct);
    }

    public static async Task AfterAsync(IAsyncTenantScopeHandle? handle, CancellationToken ct)
    {
        if (handle is not null)
        {
            await handle.CommitAsync(ct);
        }
    }

    public static async Task FinallyAsync(IAsyncTenantScopeHandle? handle)
    {
        if (handle is not null)
        {
            await handle.DisposeAsync();
        }
    }
}
