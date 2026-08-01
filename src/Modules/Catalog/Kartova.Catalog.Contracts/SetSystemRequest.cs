using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>Body of <c>PUT /catalog/{applications|services}/{id}/system</c>.
/// <c>null</c> clears the component's System membership (ADR-0096 idempotent replacement).</summary>
[ExcludeFromCodeCoverage]
public sealed record SetSystemRequest(Guid? SystemId);
