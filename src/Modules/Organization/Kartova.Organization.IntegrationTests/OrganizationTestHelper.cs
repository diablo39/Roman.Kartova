using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.IntegrationTests;

[ExcludeFromCodeCoverage]
internal static class OrganizationTestHelper
{
    /// <summary>
    /// Constructs an Organization aggregate with explicit TenantId. The production
    /// Organization.Create factory generates a fresh TenantId from the new Id; this
    /// helper uses reflection to override it so tests can write rows under a
    /// pre-seeded tenant (e.g. SeededOrgs.OrgA) inside the active tenant scope.
    /// Test-only.
    /// </summary>
    public static Kartova.Organization.Domain.Organization CreateWithTenant(Guid id, TenantId tenantId, string name)
    {
        var org = Kartova.Organization.Domain.Organization.Create(name, TimeProvider.System);

        var idProp = typeof(Kartova.Organization.Domain.Organization).GetProperty(
            nameof(Kartova.Organization.Domain.Organization.Id))!;
        idProp.SetValue(org, new Kartova.Organization.Domain.OrganizationId(id));

        var tenantProp = typeof(Kartova.Organization.Domain.Organization).GetProperty(
            nameof(Kartova.Organization.Domain.Organization.TenantId))!;
        tenantProp.SetValue(org, tenantId);

        return org;
    }
}
