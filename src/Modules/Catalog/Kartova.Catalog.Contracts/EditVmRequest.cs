using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record EditVmRequest(
    string DisplayName,
    string Description,
    string? Provider,
    VmAttributesDto Attributes);
