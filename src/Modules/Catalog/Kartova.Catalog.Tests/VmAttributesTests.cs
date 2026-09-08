using System.Text.Json;
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
    public void Validate_rejects_empty_os() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", "", 2, 4, "h", ["10.0.0.1"], "eu")));

    [TestMethod]
    public void Validate_rejects_empty_hostname() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", "linux", 2, 4, "", ["10.0.0.1"], "eu")));

    [TestMethod]
    public void Validate_rejects_empty_region() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", "linux", 2, 4, "h", ["10.0.0.1"], "")));

    [TestMethod]
    public void Validate_rejects_empty_ipAddresses() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", "linux", 2, 4, "h", [], "eu")));

    [TestMethod]
    public void Validate_rejects_nonpositive_memoryGb() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", "linux", 2, 0, "h", ["10.0.0.1"], "eu")));

    [TestMethod]
    public void Validate_rejects_os_over_max_length() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", new string('a', 129), 2, 4, "h", ["10.0.0.1"], "eu")));

    [TestMethod]
    public void Validate_rejects_hostname_over_max_length() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", "linux", 2, 4, new string('h', 256), ["10.0.0.1"], "eu")));

    [TestMethod]
    public void Validate_rejects_region_over_max_length() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto("running", "linux", 2, 4, "h", ["10.0.0.1"], new string('r', 129))));

    [TestMethod]
    public void Validate_rejects_ipAddresses_over_max_count() =>
        Assert.ThrowsExactly<ArgumentException>(() => VmAttributes.Validate(
            new VmAttributesDto(
                "running", "linux", 2, 4, "h",
                Enumerable.Range(0, 33).Select(i => $"10.0.0.{i}").ToArray(),
                "eu")));

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

    [TestMethod]
    public void ToDto_powerState_matches_ToJson_wire_value()
    {
        var a = VmAttributes.Validate(new VmAttributesDto("suspended", "linux", 2, 4, "web-01", ["10.0.0.1"], "eu-west"));
        var dto = a.ToDto();
        var json = JsonSerializer.Deserialize<JsonElement>(a.ToJson());
        Assert.AreEqual(json.GetProperty("powerState").GetString(), dto.PowerState);
    }
}
