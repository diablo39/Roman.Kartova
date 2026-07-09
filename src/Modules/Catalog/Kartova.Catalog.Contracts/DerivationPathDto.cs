using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>One provenance path explaining a derived depends-on edge: the API that links consumer→provider,
/// and (when the provider exposes it through an application) the via-application. <see cref="ViaApplicationId"/>
/// is null when the provider service provides the API directly.</summary>
[ExcludeFromCodeCoverage]
public sealed record DerivationPathDto(
    Guid ApiId, string ApiName, Guid? ViaApplicationId, string? ViaApplicationDisplayName);
