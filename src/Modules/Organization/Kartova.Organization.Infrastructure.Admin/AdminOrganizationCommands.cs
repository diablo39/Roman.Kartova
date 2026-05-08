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
        // mutation-survivor: pre-slice-6 statements; killing requires an integration
        // test that reads back the persisted row after CreateAsync. AdminBypassTests
        // only asserts the response DTO shape, not DB persistence. Pattern carries
        // forward to next Organization slice. (slice-6 mutation report 2026-05-07)
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync(ct);
        return new OrganizationDto(org.Id.Value, org.TenantId.Value, org.Name, org.CreatedAt);
    }
}
