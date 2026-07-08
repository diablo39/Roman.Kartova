using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ApiSurfaceMapperTests
{
    private static readonly Guid Api1 = Guid.NewGuid();
    private static readonly Guid Api2 = Guid.NewGuid();
    private static readonly Guid App1 = Guid.NewGuid();

    private static Dictionary<Guid, ApiSurfaceMapper.ApiMeta> Meta(params Guid[] ids) =>
        ids.ToDictionary(id => id, id => new ApiSurfaceMapper.ApiMeta($"api-{id:N}", ApiStyle.Rest, "v1", false));

    [TestMethod]
    public void Direct_provides_maps_to_direct_origin()
    {
        var result = ApiSurfaceMapper.Build(
            provides: [new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Direct, null)],
            consumesApiIds: [],
            apis: Meta(Api1),
            appNames: new Dictionary<Guid, string>());

        Assert.AreEqual(1, result.Provides.Count);
        Assert.AreEqual(ApiSurfaceOrigin.Direct, result.Provides[0].Origin);
        Assert.IsNull(result.Provides[0].ViaApplicationId);
    }

    [TestMethod]
    public void Derived_provides_carries_via_application()
    {
        var result = ApiSurfaceMapper.Build(
            provides: [new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Derived, App1)],
            consumesApiIds: [],
            apis: Meta(Api1),
            appNames: new Dictionary<Guid, string> { [App1] = "Billing" });

        var item = result.Provides.Single();
        Assert.AreEqual(ApiSurfaceOrigin.Derived, item.Origin);
        Assert.AreEqual(App1, item.ViaApplicationId);
        Assert.AreEqual("Billing", item.ViaApplicationDisplayName);
    }

    [TestMethod]
    public void Direct_wins_when_same_api_is_both_direct_and_derived()
    {
        var result = ApiSurfaceMapper.Build(
            provides:
            [
                new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Derived, App1),
                new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Direct, null),
            ],
            consumesApiIds: [],
            apis: Meta(Api1),
            appNames: new Dictionary<Guid, string> { [App1] = "Billing" });

        var item = result.Provides.Single();   // deduped to one row
        Assert.AreEqual(ApiSurfaceOrigin.Direct, item.Origin);
        Assert.IsNull(item.ViaApplicationId);
    }

    [TestMethod]
    public void Consumes_ids_map_to_direct_items()
    {
        var result = ApiSurfaceMapper.Build(
            provides: [],
            consumesApiIds: [Api2],
            apis: Meta(Api2),
            appNames: new Dictionary<Guid, string>());

        var item = result.Consumes.Single();
        Assert.AreEqual(Api2, item.ApiId);
        Assert.AreEqual(ApiSurfaceOrigin.Direct, item.Origin);
        Assert.IsNull(item.ViaApplicationId);
    }

    [TestMethod]
    public void Empty_inputs_produce_empty_lists()
    {
        var result = ApiSurfaceMapper.Build([], [], Meta(), new Dictionary<Guid, string>());
        Assert.AreEqual(0, result.Provides.Count);
        Assert.AreEqual(0, result.Consumes.Count);
    }

    [TestMethod]
    public void Metadata_is_projected_onto_items()
    {
        var apis = new Dictionary<Guid, ApiSurfaceMapper.ApiMeta>
        {
            [Api1] = new("Orders API", ApiStyle.AsyncApi, "2.0.0", true),
        };
        var result = ApiSurfaceMapper.Build(
            [new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Direct, null)], [], apis,
            new Dictionary<Guid, string>());

        var item = result.Provides.Single();
        Assert.AreEqual("Orders API", item.DisplayName);
        Assert.AreEqual(ApiStyle.AsyncApi, item.Style);
        Assert.AreEqual("2.0.0", item.Version);
        Assert.IsTrue(item.HasSpec);
    }
}
