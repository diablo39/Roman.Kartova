using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>A component's System membership after a write. Both members are <c>null</c>
/// when the component belongs to no System.</summary>
[ExcludeFromCodeCoverage]
public sealed record SystemMembershipResponse(Guid? SystemId, string? SystemDisplayName);
