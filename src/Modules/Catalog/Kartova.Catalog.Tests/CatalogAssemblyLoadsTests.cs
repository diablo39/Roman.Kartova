using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;

namespace Kartova.Catalog.Tests;

[TestClass]
public class CatalogAssemblyLoadsTests
{
    [TestMethod]
    public void All_Catalog_Marker_Types_Resolve()
    {
        Assert.IsNotNull(typeof(CatalogDomainMarker));
        Assert.IsNotNull(typeof(CatalogApplicationMarker));
        Assert.IsNotNull(typeof(CatalogInfrastructureAnchor));
        Assert.IsNotNull(typeof(CatalogContractsMarker));
    }

    [TestMethod]
    public void CatalogModule_Is_Instantiable()
    {
        var sut = new CatalogModule();
        Assert.AreEqual("catalog", sut.Name);
    }
}
