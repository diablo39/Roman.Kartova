using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>One derived depends-on relationship for a focus service: the other service (the provider for a
/// Dependencies row, the consumer for a Dependents row) plus every provenance path that links them. Read-only,
/// never persisted (ADR-0111 §Decision 5).</summary>
[ExcludeFromCodeCoverage]
public sealed record DerivedDependencyItem(
    Guid ServiceId,
    string DisplayName,
    Guid? TeamId,
    IReadOnlyList<DerivationPathDto> Paths);
