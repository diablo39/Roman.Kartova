using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Kartova.Catalog.IntegrationTests;

[ExcludeFromCodeCoverage]
[CollectionDefinition(Name)]
public sealed class KartovaApiCollection : ICollectionFixture<KartovaApiFixture>
{
    public const string Name = "Kartova Catalog API";
}
