using Kartova.Organization.Contracts;

namespace Kartova.Organization.Application;

public interface IOrganizationQueries
{
    Task<OrganizationDto?> GetCurrentAsync(CancellationToken ct);
}
