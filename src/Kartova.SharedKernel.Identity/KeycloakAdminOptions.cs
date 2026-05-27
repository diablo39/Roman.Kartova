using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Identity;

[ExcludeFromCodeCoverage]
public sealed class KeycloakAdminOptions
{
    public required string BaseUrl { get; init; }
    public required string Realm { get; init; }
    public required string AdminClientId { get; init; }
    public required string AdminClientSecret { get; init; }
    public string FrontendBaseUrl { get; init; } = "";
}
