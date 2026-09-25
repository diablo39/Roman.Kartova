using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>Metadata edit (A2) — <c>Type</c> is intentionally not a field: it is immutable on edit.</summary>
[ExcludeFromCodeCoverage]
public sealed record EditEnvironmentRequest(
    string DisplayName,
    string Description,
    string? Region,
    IReadOnlyDictionary<string, string>? ResourceDetails);
