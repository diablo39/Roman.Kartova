using Kartova.Organization.Contracts;

namespace Kartova.Organization.Infrastructure.Admin;

internal sealed class AdminOrganizationCommands : IAdminOrganizationCommands
{
    private readonly AdminOrganizationDbContext _db;

    public AdminOrganizationCommands(AdminOrganizationDbContext db)
    {
        _db = db;
    }

    public async Task<OrganizationDto> CreateAsync(string name, CancellationToken ct)
    {
        var org = Kartova.Organization.Domain.Organization.Create(name);
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync(ct);
        return new OrganizationDto(org.Id.Value, org.TenantId.Value, org.Name, org.CreatedAt);
    }
}
