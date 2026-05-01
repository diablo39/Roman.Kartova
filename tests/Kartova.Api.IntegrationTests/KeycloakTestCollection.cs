using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Kartova.Api.IntegrationTests;

/// <summary>
/// Serializes all API integration test classes that depend on Keycloak + Postgres containers.
/// Using [Collection] ensures a single shared <see cref="KeycloakContainerFixture"/> instance
/// and prevents env-var races when multiple test classes set Authentication__Authority, etc.
/// </summary>
[CollectionDefinition(Name)]
[ExcludeFromCodeCoverage]
public sealed class KeycloakTestCollection : ICollectionFixture<KeycloakContainerFixture>
{
    public const string Name = "Keycloak";
}
