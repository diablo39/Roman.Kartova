using System.Reflection;
using Kartova.Api;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Central registry of all production assemblies — updated whenever a new module is added.
/// </summary>
internal static class AssemblyRegistry
{
    public static readonly Assembly SharedKernel = typeof(IModule).Assembly;
    public static readonly Assembly Api = typeof(Program).Assembly;

    public static class Catalog
    {
        public static readonly Assembly Domain = typeof(CatalogDomainMarker).Assembly;
        public static readonly Assembly Application = typeof(CatalogApplicationMarker).Assembly;
        public static readonly Assembly Infrastructure = typeof(CatalogModule).Assembly;
        public static readonly Assembly Contracts = typeof(CatalogContractsMarker).Assembly;
    }

    public static class Organization
    {
        public static readonly Assembly Domain = typeof(Kartova.Organization.Domain.Organization).Assembly;
        public static readonly Assembly Application = typeof(IOrganizationQueries).Assembly;
        public static readonly Assembly Infrastructure = typeof(OrganizationDbContext).Assembly;
        public static readonly Assembly InfrastructureAdmin = typeof(AdminOrganizationDbContext).Assembly;
        public static readonly Assembly Contracts = typeof(OrganizationDto).Assembly;
    }

    public static IEnumerable<Assembly> AllProduction()
    {
        yield return SharedKernel;
        yield return Api;
        yield return Catalog.Domain;
        yield return Catalog.Application;
        yield return Catalog.Infrastructure;
        yield return Catalog.Contracts;
        yield return Organization.Domain;
        yield return Organization.Application;
        yield return Organization.Infrastructure;
        yield return Organization.InfrastructureAdmin;
        yield return Organization.Contracts;
    }

    public static IEnumerable<Assembly> AllContracts()
    {
        yield return Catalog.Contracts;
        yield return Organization.Contracts;
    }

    /// <summary>
    /// Returns every module's primary <c>*.Infrastructure</c> assembly.
    /// Used by <see cref="PaginationConventionRules"/> (ADR-0095 §8).
    /// Add new module Infrastructure assemblies here when modules are added.
    /// </summary>
    public static IReadOnlyList<Assembly> AllInfrastructureAssemblies() =>
    [
        Catalog.Infrastructure,
        Organization.Infrastructure,
    ];
}
