using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ListEnvironmentsHandlerFilterTests
{
    private static ListEnvironmentsQuery Query(
        EnvironmentType[]? type = null, string? region = null, string? name = null)
        => new(EnvironmentSortField.DisplayName, SortOrder.Asc, Cursor: null, Limit: 20,
            Type: type, Region: region, DisplayNameContains: name);

    [TestMethod]
    public void BuildFilterMap_is_null_when_no_filters()
        => Assert.IsNull(ListEnvironmentsHandler.BuildFilterMap(Query()));

    [TestMethod]
    public void BuildFilterMap_encodes_type_region_and_name()
    {
        var map = ListEnvironmentsHandler.BuildFilterMap(
            Query(type: [EnvironmentType.Production, EnvironmentType.Staging], region: "eu-west-1", name: "prod"));
        Assert.IsNotNull(map);
        Assert.IsTrue(map!.ContainsKey("type"));
        Assert.AreEqual("eu-west-1", map["region"]);
        Assert.AreEqual("prod", map["displayNameContains"]);
    }

    [TestMethod]
    public void BuildFilterMap_omits_absent_dimensions()
    {
        var map = ListEnvironmentsHandler.BuildFilterMap(Query(region: "eu-west-1"));
        Assert.IsNotNull(map);
        Assert.IsFalse(map!.ContainsKey("type"));
        Assert.IsFalse(map.ContainsKey("displayNameContains"));
        Assert.AreEqual("eu-west-1", map["region"]);
    }
}
