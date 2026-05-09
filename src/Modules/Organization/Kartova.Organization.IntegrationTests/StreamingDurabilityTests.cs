using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using Kartova.Testing.Auth;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Kartova.Organization.IntegrationTests;

[ExcludeFromCodeCoverage]
[TestClass]
public class StreamingDurabilityTests : OrganizationFaultInjectionTestBase
{
    [TestMethod]
    public async Task Commit_failure_on_streaming_endpoint_returns_clean_5xx()
    {
        // Endpoint filter contract: handle.CommitAsync runs BEFORE IResult.ExecuteAsync,
        // so a commit failure propagates as 500 + problem-details with no bytes
        // streamed. With middleware-only commit (the slice-2 deviation that was
        // ratified back to a hybrid two-piece adapter in 79bd0e2), the response
        // body would begin flushing during _next(context) and a commit failure
        // would surface AFTER partial bytes had already been sent.
        Fx.CommitFailFlag.Fail = true;
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" }));

            var resp = await client.GetAsync("/__test/stream");

            // commit failure must surface as 5xx, not as a partial 200 stream
            Assert.AreEqual(HttpStatusCode.InternalServerError, resp.StatusCode);
            // The streaming payload is 2 KB of zero bytes. ProblemDetails replaces
            // the body when commit throws BEFORE IResult.ExecuteAsync runs, so the
            // body must be smaller than the streamed payload (in practice it's a
            // small JSON problem-details document).
            var body = await resp.Content.ReadAsByteArrayAsync();
            // no streamed bytes should reach the client when commit fails
            Assert.IsTrue(body.Length < 2048);
        }
        finally
        {
            Fx.CommitFailFlag.Fail = false;
        }
    }
}
