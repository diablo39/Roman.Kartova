using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Identity;

[ExcludeFromCodeCoverage]
public sealed class KeycloakAdminOptions
{
    [Required, MinLength(1)]
    public required string BaseUrl { get; init; }

    [Required, MinLength(1)]
    public required string Realm { get; init; }

    [Required, MinLength(1)]
    public required string AdminClientId { get; init; }

    [Required, MinLength(1)]
    public required string AdminClientSecret { get; init; }

    [Required, MinLength(1)]
    public string FrontendBaseUrl { get; init; } = "";
}
