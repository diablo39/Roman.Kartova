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
    public void Create_rejects_content_over_cap_by_utf8_byte_count()
    {
        // 'é' (U+00E9) is 2 bytes in UTF-8; char count stays under the cap, byte count exceeds it.
        var s = new string('é', ApiSpec.MaxContentBytes / 2 + 1);
        Assert.IsTrue(s.Length <= ApiSpec.MaxContentBytes, "char length must be under the cap to prove byte-count logic");
        Assert.ThrowsExactly<ArgumentException>(
            () => ApiSpec.Create(Api, Tenant, s, ApiMediaType.ApplicationJson, User, Now));
    }

    [TestMethod]
    public void Create_rejects_unknown_media_type()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ApiSpec.Create(Api, Tenant, "{}", "text/xml", User, Now));

    [TestMethod]
    public void Replace_updates_content_and_media_type()
    {
        var s = ApiSpec.Create(Api, Tenant, "{}", ApiMediaType.ApplicationJson, User, Now);
        s.Replace("channels: {}", ApiMediaType.ApplicationYaml);
        Assert.AreEqual("channels: {}", s.Content);
        Assert.AreEqual(ApiMediaType.ApplicationYaml, s.MediaType);
    }

    [TestMethod]
    public void Create_rejects_empty_createdByUserId()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ApiSpec.Create(Api, Tenant, "{}", ApiMediaType.ApplicationJson, Guid.Empty, Now));

    [TestMethod]
    public void Replace_rejects_empty_content()
    {
        var s = ApiSpec.Create(Api, Tenant, "{}", ApiMediaType.ApplicationJson, User, Now);
        Assert.ThrowsExactly<ArgumentException>(() => s.Replace("   ", ApiMediaType.ApplicationJson));
    }

    [TestMethod]
    public void Replace_rejects_unknown_media_type()
    {
        var s = ApiSpec.Create(Api, Tenant, "{}", ApiMediaType.ApplicationJson, User, Now);
        Assert.ThrowsExactly<ArgumentException>(() => s.Replace("{}", "text/xml"));
    }

    [TestMethod]
    public void Create_accepts_content_exactly_at_cap()
    {
        var atCap = new string('x', ApiSpec.MaxContentBytes);   // exactly MaxContentBytes UTF-8 bytes
        var s = ApiSpec.Create(Api, Tenant, atCap, ApiMediaType.ApplicationJson, User, Now);
        Assert.AreEqual(ApiSpec.MaxContentBytes, System.Text.Encoding.UTF8.GetByteCount(s.Content));
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
