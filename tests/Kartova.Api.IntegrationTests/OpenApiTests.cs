using System.Net;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kartova.Api.IntegrationTests;

[TestClass]
public class OpenApiTests : KeycloakContainerTestBase
{
    private WebApplicationFactory<Program>? _app;

    [TestInitialize]
    public void InitializeAsync()
    {
        // Env vars must be set BEFORE the WebApplicationFactory boots the host.
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.AppRole));
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole));
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), Containers.KeycloakAuthority);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.MetadataAddress),
            $"{Containers.KeycloakAuthority}/.well-known/openid-configuration");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), "kartova-api");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");

        // CORS allowlist — set before WAF boots so the policy builder sees the value.
        Environment.SetEnvironmentVariable($"{CorsConfigKeys.AllowedOrigins.Replace(":", "__")}__0", "http://localhost:5173");

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
        });
    }

    [TestCleanup]
    public void DisposeAsync()
    {
        _app?.Dispose();
    }

    [TestMethod]
    public async Task OpenApiDocument_IsReachableAndParsesAsJson()
    {
        var client = _app!.CreateClient();
        var resp = await client.GetAsync("/openapi/v1.json");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        Assert.AreEqual("application/json", resp.Content.Headers.ContentType?.MediaType);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(body));
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        StringAssert.StartsWith(doc.RootElement.GetProperty("openapi").GetString(), "3.");
        Assert.AreNotEqual(
            System.Text.Json.JsonValueKind.Undefined,
            doc.RootElement.GetProperty("paths").GetProperty("/api/v1/version").ValueKind,
            "the version endpoint must be advertised in the OpenAPI document.");
    }

    [TestMethod]
    public async Task ListApplications_query_parameter_schemas_match_runtime_contract()
    {
        // ADR-0095 §Consequences: OpenAPI generates per-resource sort-field enums
        // (SortByApplications, SortOrder) and a bounded-integer ?limit so the frontend
        // gets compile-time-safe sort values + range hints via openapi-typescript
        // codegen. Surfacing happens via CursorListQueryParameterTransformer.
        var client = _app!.CreateClient();
        var resp = await client.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var parameters = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/catalog/applications")
            .GetProperty("get")
            .GetProperty("parameters");

        var sortByEnum = ParameterEnum(parameters, "sortBy");
        CollectionAssert.AreEquivalent(new[] { "createdAt", "name" }, sortByEnum.ToList());

        var sortOrderEnum = ParameterEnum(parameters, "sortOrder");
        CollectionAssert.AreEquivalent(new[] { "asc", "desc" }, sortOrderEnum.ToList());

        var limitSchema = ParameterSchema(parameters, "limit");
        Assert.AreEqual("integer", limitSchema.GetProperty("type").GetString());
        Assert.AreEqual("int32", limitSchema.GetProperty("format").GetString());
        Assert.AreEqual(1, limitSchema.GetProperty("minimum").GetInt32());
        Assert.AreEqual(200, limitSchema.GetProperty("maximum").GetInt32());
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
