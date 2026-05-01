using FluentAssertions;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Kartova.Api.IntegrationTests;

[Collection(KeycloakTestCollection.Name)]
public class CorsTests : IAsyncLifetime
{
    private readonly KeycloakContainerFixture _fx;
    private WebApplicationFactory<Program>? _app;

    public CorsTests(KeycloakContainerFixture fx) => _fx = fx;

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
    public async Task Preflight_FromConfiguredOrigin_AllowsRequest()
    {
        var client = _app!.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/version");
        req.Headers.Add("Origin", "http://localhost:5173");
        req.Headers.Add("Access-Control-Request-Method", "GET");

        var resp = await client.SendAsync(req);

        resp.Headers.Should().ContainKey("Access-Control-Allow-Origin");
        resp.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle().Which.Should().Be("http://localhost:5173");
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_DoesNotEchoOrigin()
    {
        var client = _app!.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/version");
        req.Headers.Add("Origin", "https://evil.example");
        req.Headers.Add("Access-Control-Request-Method", "GET");

        var resp = await client.SendAsync(req);

        resp.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse(
            "the API must not echo origins outside the configured allowlist.");
    }

    private static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
