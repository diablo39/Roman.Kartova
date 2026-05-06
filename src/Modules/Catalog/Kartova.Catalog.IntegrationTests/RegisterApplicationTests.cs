using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

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
            new RegisterApplicationRequest("payments-api", "Payments API", "Payments REST surface."));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.ToString().Should().StartWith("/api/v1/catalog/applications/");

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("payments-api");
        body.DisplayName.Should().Be("Payments API");
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
            new RegisterApplicationRequest("svc-x", "Svc X", "x"));
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
            new RegisterApplicationRequest("svc-y", "Svc Y", "y"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();
        body!.TenantId.Should().Be(tenantFromToken);
    }

    [Theory]
    [InlineData("", "Display", "desc")]
    [InlineData("   ", "Display", "desc")]
    [InlineData("name", "", "desc")]
    [InlineData("name", "  ", "desc")]
    [InlineData("name", "Display", "")]
    [InlineData("name", "Display", "  ")]
    [InlineData("BadName", "Display", "desc")]      // kebab-case: uppercase
    [InlineData("bad_name", "Display", "desc")]     // underscore
    [InlineData("bad name", "Display", "desc")]     // space
    [InlineData("9digit", "Display", "desc")]       // leading digit
    public async Task POST_with_invalid_payload_returns_400(string name, string displayName, string description)
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GET_by_id_returns_row_in_same_tenant()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var post = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-z", "Svc Z", "z"));
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
    public async Task GET_list_returns_apps_in_current_tenant_sorted_by_createdAt()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var first = await CreateApp(client, "first-app-list");
        var second = await CreateApp(client, "second-app-list");

        var resp = await client.GetAsync("/api/v1/catalog/applications");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();

        page.Should().NotBeNull();
        page!.Items.Select(x => x.Id).Should().Contain(new[] { first.Id, second.Id });
        // Default sort is createdAt desc — both newly created apps appear at the front.
        page!.Items.Should().NotBeEmpty();
    }

    private static async Task<ApplicationResponse> CreateApp(HttpClient c, string name)
    {
        var post = await c.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, name, $"desc for {name}"));
        return (await post.Content.ReadFromJsonAsync<ApplicationResponse>())!;
    }

    [Fact]
    public async Task POST_with_invalid_displayName_returns_field_level_problem_details()
    {
        var client = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-fl", "", "desc"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var errors = doc.GetProperty("errors");
        errors.GetProperty("displayName").EnumerateArray().Single().GetString()
            .Should().Contain("must not be empty");
    }

    [Fact]
    public async Task POST_without_token_returns_401()
    {
        using var client = _fx.CreateAnonymousClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("name", "Name", "desc"));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_by_id_returns_404_for_other_tenants_row()
    {
        // OrgA creates a row, OrgB tries to fetch it by id. Must 404 — never leak
        // existence (no 403, no 200). Pins the cross-tenant isolation guarantee.
        var clientA = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var post = await clientA.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("orga-private", "Orga Private", "owned by orga"));
        post.StatusCode.Should().Be(HttpStatusCode.Created);
        var orgaApp = await post.Content.ReadFromJsonAsync<ApplicationResponse>();

        var clientB = await _fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await clientB.GetAsync($"/api/v1/catalog/applications/{orgaApp!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_list_excludes_other_tenants_rows()
    {
        // OrgA seeds a row; OrgB's list must not include it. RLS + tenant scope
        // together must shield orgb from any orga rows.
        var clientA = await _fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var orgaApp = await CreateApp(clientA, "orga-isolation-probe");

        var clientB = await _fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await clientB.GetAsync("/api/v1/catalog/applications");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();

        page.Should().NotBeNull();
        var rows = page!.Items;
        rows.Select(x => x.Id).Should().NotContain(orgaApp.Id);

        var orgbTenantId = await _fx.GetTenantIdClaimAsync("admin@orgb.kartova.local");
        rows.All(x => x.TenantId == orgbTenantId).Should().BeTrue();
    }
}
