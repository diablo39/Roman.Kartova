using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ApiSpecTests
{
    private static readonly ApiId Api = ApiId.New();
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [TestMethod]
    public void Create_valid_json_spec()
    {
        var s = ApiSpec.Create(Api, Tenant, "{}", ApiMediaType.ApplicationJson, User, Now);
        Assert.AreEqual("{}", s.Content);
        Assert.AreEqual(ApiMediaType.ApplicationJson, s.MediaType);
        Assert.AreEqual(Api.Value, s.ApiId.Value);
    }

    [TestMethod]
    public void Create_rejects_empty_content()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ApiSpec.Create(Api, Tenant, "   ", ApiMediaType.ApplicationJson, User, Now));

    [TestMethod]
    public void Create_rejects_oversized_content()
    {
        var big = new string('x', ApiSpec.MaxContentBytes + 1);
        Assert.ThrowsExactly<ArgumentException>(
            () => ApiSpec.Create(Api, Tenant, big, ApiMediaType.ApplicationJson, User, Now));
    }

    [TestMethod]
    public void Create_rejects_unknown_media_type()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ApiSpec.Create(Api, Tenant, "{}", "text/xml", User, Now));

    [TestMethod]
    public void Replace_updates_content_and_media_type()
    {
        var s = ApiSpec.Create(Api, Tenant, "{}", ApiMediaType.ApplicationJson, User, Now);
        s.Replace("channels: {}", ApiMediaType.ApplicationYaml, User, Now.AddMinutes(1));
        Assert.AreEqual("channels: {}", s.Content);
        Assert.AreEqual(ApiMediaType.ApplicationYaml, s.MediaType);
    }

    [TestMethod]
    public void IsAllowed_matches_only_json_and_yaml()
    {
        Assert.IsTrue(ApiMediaType.IsAllowed("application/json"));
        Assert.IsTrue(ApiMediaType.IsAllowed("application/yaml"));
        Assert.IsFalse(ApiMediaType.IsAllowed("text/xml"));
        Assert.IsFalse(ApiMediaType.IsAllowed(""));
    }
}
