using System.Collections.Frozen;

namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaPermissions
{
    public const string CatalogRead = "catalog.read";
    public const string CatalogApplicationsRegister = "catalog.applications.register";
    public const string CatalogApplicationsEditMetadata = "catalog.applications.edit-metadata";
    public const string CatalogApplicationsLifecycleForward = "catalog.applications.lifecycle.forward";
    public const string CatalogApplicationsLifecycleReverse = "catalog.applications.lifecycle.reverse";

    public static FrozenSet<string> All { get; } = new[]
    {
        CatalogRead,
        CatalogApplicationsRegister,
        CatalogApplicationsEditMetadata,
        CatalogApplicationsLifecycleForward,
        CatalogApplicationsLifecycleReverse,
    }.ToFrozenSet(StringComparer.Ordinal);
}
