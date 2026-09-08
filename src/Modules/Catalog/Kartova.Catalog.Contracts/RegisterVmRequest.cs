using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record RegisterVmRequest(
    string DisplayName,
    string Description,
    Guid TeamId,
    VmAttributesDto Attributes);
