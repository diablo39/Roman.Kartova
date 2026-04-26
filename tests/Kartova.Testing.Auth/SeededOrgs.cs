using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Testing.Auth;

[ExcludeFromCodeCoverage]
public static class SeededOrgs
{
    public static readonly TenantId OrgA = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    public static readonly TenantId OrgB = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
}
