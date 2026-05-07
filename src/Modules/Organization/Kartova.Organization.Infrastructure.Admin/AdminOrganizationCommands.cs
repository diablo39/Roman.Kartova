using Kartova.Organization.Application;
using Kartova.Organization.Contracts;

namespace Kartova.Organization.Infrastructure.Admin;

internal sealed class AdminOrganizationCommands : IAdminOrganizationCommands
{
    private readonly AdminOrganizationDbContext _db;
    private readonly TimeProvider _clock;

    public AdminOrganizationCommands(AdminOrganizationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<OrganizationDto> CreateAsync(string name, CancellationToken ct)
    {
        var org = Kartova.Organization.Domain.Organization.Create(name, _clock);
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync(ct);
        return new OrganizationDto(org.Id.Value, org.TenantId.Value, org.Name, org.CreatedAt);
    }
}
