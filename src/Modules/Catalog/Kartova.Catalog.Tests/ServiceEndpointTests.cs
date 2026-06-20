using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ServiceEndpointTests
{
    [TestMethod]
    public void Ctor_with_valid_absolute_url_and_protocol_sets_properties()
    {
        var ep = new ServiceEndpoint("https://api.example.com/v1", Protocol.Rest);
        Assert.AreEqual("https://api.example.com/v1", ep.Url);
        Assert.AreEqual(Protocol.Rest, ep.Protocol);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Ctor_throws_on_empty_url(string url) =>
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceEndpoint(url, Protocol.Rest));

    [TestMethod]
    public void Ctor_throws_on_relative_url() =>
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceEndpoint("/v1/orders", Protocol.Rest));

    [TestMethod]
    public void Ctor_throws_on_url_over_2048_chars() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new ServiceEndpoint("https://x/" + new string('a', 2048), Protocol.Rest));

    [TestMethod]
    public void Ctor_throws_on_undefined_protocol() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new ServiceEndpoint("https://api.example.com", (Protocol)999));
}
