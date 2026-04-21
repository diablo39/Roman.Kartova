using FluentAssertions;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Xunit;

namespace Kartova.Catalog.Tests;

public class CatalogAssemblyLoadsTests
{
    [Fact]
    public void All_Catalog_Marker_Types_Resolve()
    {
        typeof(CatalogDomainMarker).Should().NotBeNull();
        typeof(CatalogApplicationMarker).Should().NotBeNull();
        typeof(CatalogInfrastructureAnchor).Should().NotBeNull();
        typeof(CatalogContractsMarker).Should().NotBeNull();
    }

    [Fact]
    public void CatalogModule_Is_Instantiable()
    {
        var sut = new CatalogModule();
        sut.Name.Should().Be("catalog");
    }
}
