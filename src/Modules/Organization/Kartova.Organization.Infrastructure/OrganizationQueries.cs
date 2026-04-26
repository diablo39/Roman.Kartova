using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

internal sealed class OrganizationQueries : IOrganizationQueries
{
    private readonly OrganizationDbContext _db;

    public OrganizationQueries(OrganizationDbContext db)
    {
        _db = db;
    }

    public async Task<OrganizationDto?> GetCurrentAsync(CancellationToken ct)
    {
        // RLS filters rows to the current tenant. Expect 0 or 1 row for the current tenant.
        var row = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(ct);
        if (row is null) return null;
        return new OrganizationDto(row.Id.Value, row.TenantId.Value, row.Name, row.CreatedAt);
    }
}
