using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>
/// Sets or clears the successor of a Deprecated Application (ADR-0110 §5.3).
/// PUT semantics — idempotent replacement; <see langword="null"/>
/// <see cref="SuccessorApplicationId"/> clears the successor.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record SetApplicationSuccessorRequest(Guid? SuccessorApplicationId);
