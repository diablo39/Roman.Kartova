using Kartova.Organization.Contracts;

namespace Kartova.Organization.Application;

public interface IAdminOrganizationCommands
{
    Task<OrganizationDto> CreateAsync(string name, CancellationToken ct);
}
