using System.Net;
using FluentAssertions;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Kartova.Api.IntegrationTests;

[Collection(KeycloakTestCollection.Name)]
public class OpenApiTests : IAsyncLifetime
{
    private readonly KeycloakContainerFixture _fx;
    private WebApplicationFactory<Program>? _app;

    public OpenApiTests(KeycloakContainerFixture fx) => _fx = fx;

    public Task InitializeAsync()
    {
        // Env vars must be set BEFORE the WebApplicationFactory boots the host.
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}",
            PostgresTestBootstrap.ConnectionStringFor(_fx.Postgres.GetConnectionString(), PostgresTestBootstrap.AppRole));
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}",
            PostgresTestBootstrap.ConnectionStringFor(_fx.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole));
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), _fx.KeycloakAuthority);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.MetadataAddress),
            $"{_fx.KeycloakAuthority}/.well-known/openid-configuration");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), "kartova-api");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");

        // CORS allowlist — set before WAF boots so the policy builder sees the value.
        Environment.SetEnvironmentVariable($"{CorsConfigKeys.AllowedOrigins.Replace(":", "__")}__0", "http://localhost:5173");

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task OpenApiDocument_IsReachableAndParsesAsJson()
    {
        var client = _app!.CreateClient();
        var resp = await client.GetAsync("/openapi/v1.json");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.GetProperty("paths").GetProperty("/api/v1/version")
            .ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined,
                "the version endpoint must be advertised in the OpenAPI document.");
    }

    [Fact]
    public async Task ListApplications_query_parameter_schemas_match_runtime_contract()
    {
        // ADR-0095 §Consequences: OpenAPI generates per-resource sort-field enums
        // (SortByApplications, SortOrder) and a bounded-integer ?limit so the frontend
        // gets compile-time-safe sort values + range hints via openapi-typescript
        // codegen. Surfacing happens via CursorListQueryParameterTransformer.
        var client = _app!.CreateClient();
        var resp = await client.GetAsync("/openapi/v1.json");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var parameters = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/catalog/applications")
            .GetProperty("get")
            .GetProperty("parameters");

        var sortByEnum = ParameterEnum(parameters, "sortBy");
        sortByEnum.Should().BeEquivalentTo(["createdAt", "name"]);

        var sortOrderEnum = ParameterEnum(parameters, "sortOrder");
        sortOrderEnum.Should().BeEquivalentTo(["asc", "desc"]);

        var limitSchema = ParameterSchema(parameters, "limit");
        limitSchema.GetProperty("type").GetString().Should().Be("integer");
        limitSchema.GetProperty("format").GetString().Should().Be("int32");
        limitSchema.GetProperty("minimum").GetInt32().Should().Be(1);
        limitSchema.GetProperty("maximum").GetInt32().Should().Be(200);
    }

    private static IReadOnlyList<string> ParameterEnum(System.Text.Json.JsonElement parameters, string name)
    {
        var schema = ParameterSchema(parameters, name);
        return schema.GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();
    }

    private static System.Text.Json.JsonElement ParameterSchema(System.Text.Json.JsonElement parameters, string name)
    {
        foreach (var p in parameters.EnumerateArray())
        {
            if (p.GetProperty("name").GetString() == name)
            {
                return p.GetProperty("schema");
            }
        }
        throw new InvalidOperationException($"Parameter '{name}' not found.");
    }

    private static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
