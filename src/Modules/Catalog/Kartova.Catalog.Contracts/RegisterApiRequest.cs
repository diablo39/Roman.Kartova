using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record RegisterApiRequest(
    string DisplayName,
    string Description,
    ApiStyle Style,
    string Version,
    string? SpecUrl,
    Guid TeamId);
