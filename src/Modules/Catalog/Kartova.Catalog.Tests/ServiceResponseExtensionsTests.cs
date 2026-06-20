using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ServiceResponseExtensionsTests
{
    [TestMethod]
    public void ToResponse_maps_all_fields_and_endpoints()
    {
        var tenant = new TenantId(Guid.NewGuid());
        var team = Guid.NewGuid();
        var creator = Guid.NewGuid();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-20T10:00:00Z"));
        var svc = Service.Create("orders-svc", "Orders.", creator, team,
            new[] { new ServiceEndpoint("https://api.example.com", Protocol.Grpc) }, tenant, clock);

        var resp = svc.ToResponse();

        Assert.AreEqual(svc.Id.Value, resp.Id);
        Assert.AreEqual(tenant.Value, resp.TenantId);
        Assert.AreEqual("orders-svc", resp.DisplayName);
        Assert.AreEqual(team, resp.TeamId);
        Assert.AreEqual(HealthStatus.Unknown, resp.Health);
        Assert.AreEqual(1, resp.Endpoints.Count);
        Assert.AreEqual("https://api.example.com", resp.Endpoints[0].Url);
        Assert.AreEqual(Protocol.Grpc, resp.Endpoints[0].Protocol);
        Assert.IsNull(resp.CreatedBy);
    }
}
