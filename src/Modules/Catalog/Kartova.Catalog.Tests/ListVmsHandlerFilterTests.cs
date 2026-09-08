using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Unit-tier tests for <see cref="ListVmsHandler"/>'s JSON-containment builders and cursor
/// f-map projection (ADR-0107/ADR-0095). These are pure string/dictionary builders — no DB
/// involved — so EF translation of <c>EF.Functions.JsonContains</c> is NOT exercised here
/// (that is Task 12's real-Postgres integration coverage).
/// <para>
/// Camelcase keys under test MUST stay exactly <c>powerState/os/region/hostname/ipAddresses</c>
/// (object containment) since <see cref="VmAttributes.ToJson"/> serializes with those same keys.
/// </para>
/// </summary>
[TestClass]
public class ListVmsHandlerFilterTests
{
    private static readonly Guid TeamA = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static ListVmsQuery Query(
        Guid[]? teamId = null, string? powerState = null, string? os = null,
        string? region = null, string? hostname = null, string? ipAddress = null, int limit = 50) =>
        new(InfrastructureSortField.DisplayName, SortOrder.Asc, Cursor: null, Limit: limit,
            TeamId: teamId ?? [],
            PowerState: powerState, Os: os, Region: region, Hostname: hostname, IpAddress: ipAddress);

    [TestMethod]
    public void Contains_builds_single_key_object_containment_json() =>
        Assert.AreEqual("{\"powerState\":\"running\"}", ListVmsHandler.Contains("powerState", "running"));

    [TestMethod]
    public void Contains_escapes_the_supplied_value() =>
        Assert.AreEqual("{\"hostname\":\"web-\\u002201\\u0022\"}", ListVmsHandler.Contains("hostname", "web-\"01\""));

    [TestMethod]
    public void ContainsArray_builds_single_element_array_containment_json() =>
        Assert.AreEqual("{\"ipAddresses\":[\"10.0.0.1\"]}", ListVmsHandler.ContainsArray("ipAddresses", "10.0.0.1"));

    [TestMethod]
    public void BuildFilterMap_with_no_filters_returns_null() =>
        Assert.IsNull(ListVmsHandler.BuildFilterMap(Query()));

    [TestMethod]
    public void BuildFilterMap_with_empty_teamId_and_no_other_filters_returns_null() =>
        Assert.IsNull(ListVmsHandler.BuildFilterMap(Query(teamId: [])));

    [TestMethod]
    public void BuildFilterMap_with_teamId_sets_only_teamId_key()
    {
        var map = ListVmsHandler.BuildFilterMap(Query(teamId: [TeamA]));
        Assert.IsNotNull(map);
        Assert.AreEqual(1, map!.Count);
        Assert.AreEqual(TeamA.ToString("D"), map["teamId"]);
    }

    [TestMethod]
    public void BuildFilterMap_with_powerState_sets_only_powerState_key()
    {
        var map = ListVmsHandler.BuildFilterMap(Query(powerState: "running"));
        Assert.IsNotNull(map);
        Assert.AreEqual(1, map!.Count);
        Assert.AreEqual("running", map["powerState"]);
    }

    [TestMethod]
    public void BuildFilterMap_with_os_sets_only_os_key()
    {
        var map = ListVmsHandler.BuildFilterMap(Query(os: "linux"));
        Assert.IsNotNull(map);
        Assert.AreEqual(1, map!.Count);
        Assert.AreEqual("linux", map["os"]);
    }

    [TestMethod]
    public void BuildFilterMap_with_region_sets_only_region_key()
    {
        var map = ListVmsHandler.BuildFilterMap(Query(region: "eu-west"));
        Assert.IsNotNull(map);
        Assert.AreEqual(1, map!.Count);
        Assert.AreEqual("eu-west", map["region"]);
    }

    [TestMethod]
    public void BuildFilterMap_with_hostname_sets_only_hostname_key()
    {
        var map = ListVmsHandler.BuildFilterMap(Query(hostname: "web-01"));
        Assert.IsNotNull(map);
        Assert.AreEqual(1, map!.Count);
        Assert.AreEqual("web-01", map["hostname"]);
    }

    [TestMethod]
    public void BuildFilterMap_with_ipAddress_sets_only_ipAddresses_key()
    {
        var map = ListVmsHandler.BuildFilterMap(Query(ipAddress: "10.0.0.1"));
        Assert.IsNotNull(map);
        Assert.AreEqual(1, map!.Count);
        Assert.AreEqual("10.0.0.1", map["ipAddresses"]);
    }

    [TestMethod]
    public void BuildFilterMap_with_every_dim_sets_all_keys()
    {
        var map = ListVmsHandler.BuildFilterMap(Query(
            teamId: [TeamA], powerState: "running", os: "linux",
            region: "eu-west", hostname: "web-01", ipAddress: "10.0.0.1"));
        Assert.IsNotNull(map);
        Assert.AreEqual(6, map!.Count);
        Assert.AreEqual(TeamA.ToString("D"), map["teamId"]);
        Assert.AreEqual("running", map["powerState"]);
        Assert.AreEqual("linux", map["os"]);
        Assert.AreEqual("eu-west", map["region"]);
        Assert.AreEqual("web-01", map["hostname"]);
        Assert.AreEqual("10.0.0.1", map["ipAddresses"]);
    }
}
