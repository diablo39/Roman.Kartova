using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

[ExcludeFromCodeCoverage]
[Collection(KartovaApiFaultInjectionCollection.Name)]
public class StreamingDurabilityTests
{
    private readonly KartovaApiFaultInjectionFixture _fx;

    public StreamingDurabilityTests(KartovaApiFaultInjectionFixture fx) => _fx = fx;

    [Fact]
    public async Task Commit_failure_on_streaming_endpoint_returns_clean_5xx()
    {
        // Endpoint filter contract: handle.CommitAsync runs BEFORE IResult.ExecuteAsync,
        // so a commit failure propagates as 500 + problem-details with no bytes
        // streamed. With middleware-only commit (the slice-2 deviation that was
        // ratified back to a hybrid two-piece adapter in 79bd0e2), the response
        // body would begin flushing during _next(context) and a commit failure
        // would surface AFTER partial bytes had already been sent.
        _fx.CommitFailFlag.Fail = true;
        try
        {
            var client = _fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" }));

            var resp = await client.GetAsync("/__test/stream");

            resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
                because: "commit failure must surface as 5xx, not as a partial 200 stream");
            // The streaming payload is 2 KB of zero bytes. ProblemDetails replaces
            // the body when commit throws BEFORE IResult.ExecuteAsync runs, so the
            // body must be smaller than the streamed payload (in practice it's a
            // small JSON problem-details document).
            var body = await resp.Content.ReadAsByteArrayAsync();
            body.Length.Should().BeLessThan(2048,
                because: "no streamed bytes should reach the client when commit fails");
        }
        finally
        {
            _fx.CommitFailFlag.Fail = false;
        }
    }
}
