using System.Reflection;
using Kartova.Api;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
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

    public static IEnumerable<Assembly> AllProduction()
    {
        yield return SharedKernel;
        yield return Api;
        yield return Catalog.Domain;
        yield return Catalog.Application;
        yield return Catalog.Infrastructure;
        yield return Catalog.Contracts;
    }
}
