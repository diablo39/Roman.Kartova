using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.IntegrationTests;

[Collection(KartovaApiCollection.Name)]
public class RegisterApplicationTests
{
    private readonly KartovaApiFixture _fx;

    public RegisterApplicationTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task POST_with_valid_payload_creates_row_and_returns_201()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("payments-api", "Payments REST surface."));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.ToString().Should().StartWith("/api/v1/catalog/applications/");

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("payments-api");
        body.Description.Should().Be("Payments REST surface.");
        body.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task POST_persists_owner_user_id_from_jwt_sub_claim()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var subFromToken = await _fx.GetSubClaimAsync("admin@orga.kartova.local");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-x", "x"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();
        body!.OwnerUserId.Should().Be(subFromToken);
    }

    [Fact]
    public async Task POST_persists_tenant_id_from_scope_not_payload()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var tenantFromToken = await _fx.GetTenantIdClaimAsync("admin@orga.kartova.local");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-y", "y"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();
        body!.TenantId.Should().Be(tenantFromToken);
    }

    [Theory]
    [InlineData("", "desc")]
    [InlineData("   ", "desc")]
    [InlineData("name", "")]
    [InlineData("name", "  ")]
    public async Task POST_with_invalid_payload_returns_400(string name, string description)
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, description));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GET_by_id_returns_row_in_same_tenant()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var post = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-z", "z"));
        var created = await post.Content.ReadFromJsonAsync<ApplicationResponse>();

        var get = await client.GetAsync($"/api/v1/catalog/applications/{created!.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await get.Content.ReadFromJsonAsync<ApplicationResponse>();
        fetched!.Id.Should().Be(created.Id);
        fetched.Name.Should().Be("svc-z");
    }

    [Fact]
    public async Task GET_by_id_returns_404_for_unknown_id()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.GetAsync($"/api/v1/catalog/applications/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task POST_without_token_returns_401()
    {
        using var client = _fx.CreateAnonymousClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("name", "desc"));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
