using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ServiceTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Team = Guid.NewGuid();
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-06-20T10:00:00Z"));

    private static ServiceEndpoint Ep(string u = "https://api.example.com/v1") => new(u, Protocol.Rest);

    [TestMethod]
    public void Create_with_valid_args_sets_fields_and_defaults_health_unknown()
    {
        var s = Service.Create("orders-svc", "Order service.", Creator, Team, new[] { Ep() }, Tenant, Clock);

        Assert.AreEqual("orders-svc", s.DisplayName);
        Assert.AreEqual("Order service.", s.Description);
        Assert.AreEqual(Creator, s.CreatedByUserId);
        Assert.AreEqual(Team, s.TeamId);
        Assert.AreEqual(Tenant, s.TenantId);
        Assert.AreEqual(HealthStatus.Unknown, s.Health);
        Assert.AreEqual(1, s.Endpoints.Count);
        Assert.AreEqual(Clock.GetUtcNow(), s.CreatedAt);
        Assert.AreNotEqual(Guid.Empty, s.Id.Value);
    }

    [TestMethod]
    public void Create_allows_zero_endpoints()
    {
        var s = Service.Create("svc", "No endpoints yet.", Creator, Team, Array.Empty<ServiceEndpoint>(), Tenant, Clock);
        Assert.AreEqual(0, s.Endpoints.Count);
    }

    [TestMethod]
    public void Create_preserves_endpoint_order()
    {
        var a = Ep("https://a.example.com");
        var b = Ep("https://b.example.com");
        var s = Service.Create("svc", "desc", Creator, Team, new[] { a, b }, Tenant, Clock);
        Assert.AreEqual("https://a.example.com", s.Endpoints[0].Url);
        Assert.AreEqual("https://b.example.com", s.Endpoints[1].Url);
    }

    [TestMethod]
    public void Create_throws_when_endpoints_exceed_50()
    {
        var many = Enumerable.Range(0, 51).Select(i => Ep($"https://h{i}.example.com")).ToArray();
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", "desc", Creator, Team, many, Tenant, Clock));
    }

    [TestMethod]
    public void Create_allows_exactly_50_endpoints()
    {
        var fifty = Enumerable.Range(0, 50).Select(i => Ep($"https://h{i}.example.com")).ToArray();
        var s = Service.Create("svc", "desc", Creator, Team, fifty, Tenant, Clock);
        Assert.AreEqual(50, s.Endpoints.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_display_name(string name) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create(name, "desc", Creator, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    public void Create_throws_on_display_name_over_128() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create(new string('x', 129), "desc", Creator, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_description(string desc) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", desc, Creator, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    public void Create_throws_on_description_over_4096() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", new string('x', 4097), Creator, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    public void Create_throws_on_empty_created_by() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", "desc", Guid.Empty, Team, new[] { Ep() }, Tenant, Clock));

    [TestMethod]
    public void Create_throws_on_empty_team() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => Service.Create("svc", "desc", Creator, Guid.Empty, new[] { Ep() }, Tenant, Clock));

    // F2: boundary — exactly 128 chars is accepted
    [TestMethod]
    public void Create_accepts_display_name_of_exactly_128_chars()
    {
        var name = new string('x', 128);
        var s = Service.Create(name, "desc", Creator, Team, new[] { Ep() }, Tenant, Clock);
        Assert.AreEqual(128, s.DisplayName.Length);
    }

    // F3: boundary — exactly 4096 chars description is accepted
    [TestMethod]
    public void Create_accepts_description_of_exactly_4096_chars()
    {
        var desc = new string('d', 4096);
        var s = Service.Create("svc", desc, Creator, Team, new[] { Ep() }, Tenant, Clock);
        Assert.AreEqual(4096, s.Description.Length);
    }

    // F4: null endpoints coerces to empty list (kills ?? new List mutant)
    [TestMethod]
    public void Create_with_null_endpoints_returns_service_with_zero_endpoints()
    {
        IEnumerable<ServiceEndpoint>? nullEndpoints = null;
        var s = Service.Create("svc", "desc", Creator, Team, nullEndpoints!, Tenant, Clock);
        Assert.AreEqual(0, s.Endpoints.Count);
    }

    // F5: null TimeProvider throws ArgumentNullException (kills ThrowIfNull mutant)
    [TestMethod]
    public void Create_with_null_TimeProvider_throws_ArgumentNullException()
    {
        TimeProvider? nullClock = null;
        Assert.ThrowsExactly<ArgumentNullException>(
            () => Service.Create("svc", "desc", Creator, Team, new[] { Ep() }, Tenant, nullClock!));
    }
}
