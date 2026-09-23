using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record RegisterEnvironmentRequest(
    string DisplayName,
    string Description,
    EnvironmentType Type,
    string? Region,
    IReadOnlyDictionary<string, string>? ResourceDetails);
