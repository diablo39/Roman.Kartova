using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.Tests;

[TestClass]
public class VmAttributesTests
{
    [TestMethod]
    public void Validate_rejects_bad_power_state() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("nope", "linux", 2, 4, "h", ["10.0.0.1"], "eu")));

    [TestMethod]
    public void Validate_rejects_nonpositive_vcpu() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", "linux", 0, 4, "h", ["10.0.0.1"], "eu")));

    [TestMethod]
    public void Validate_rejects_invalid_ip() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", "linux", 2, 4, "h", ["not-an-ip"], "eu")));

    [TestMethod]
    public void Roundtrip_json_is_camelcase()
    {
        var a = VmAttributes.Validate(new VmAttributesDto("running", "linux", 2, 4, "web-01", ["10.0.0.1", "10.0.0.2"], "eu-west"));
        var json = a.ToJson();
        StringAssert.Contains(json, "\"powerState\":\"running\"");
        StringAssert.Contains(json, "\"ipAddresses\":[");
        var back = VmAttributes.FromJson(json);
        Assert.AreEqual(a, back);
    }
}
