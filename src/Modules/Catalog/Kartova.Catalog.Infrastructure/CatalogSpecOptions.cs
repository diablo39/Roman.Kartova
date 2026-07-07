using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Operator-tunable bounds for stored API spec documents (ADR-0112).
/// Bound from configuration section <see cref="SectionName"/>; default 5 MiB.</summary>
[ExcludeFromCodeCoverage]
public sealed class CatalogSpecOptions
{
    public const string SectionName = "Catalog:ApiSpec";

    /// <summary>Maximum UTF-8 byte length of a stored spec. Enforced at the upload
    /// endpoint (declared-length pre-check + streamed read cap). Validated into
    /// [1 KiB, 50 MiB] by <see cref="CatalogSpecOptionsValidator"/>.</summary>
    public int MaxContentBytes { get; set; } = 5 * 1024 * 1024;
}
