using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

[ExcludeFromCodeCoverage]
[CollectionDefinition(Name)]
public sealed class KartovaApiFaultInjectionCollection : ICollectionFixture<KartovaApiFaultInjectionFixture>
{
    public const string Name = "Kartova API (Fault Injection)";
}
