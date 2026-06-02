using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Organization.Contracts;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Slice-9 carry-forward MT3 — end-to-end wire test for the
/// <c>POST /api/v1/organizations/invitations</c> 502 ServiceUnavailable branch:
/// when the KeyCloak admin client throws <see cref="KeycloakAdminError.Unexpected"/>
/// on <c>AssignRealmRoleAsync</c>, the handler returns <c>Failed(Upstream)</c>
/// and the endpoint maps that to 502 + <c>ProblemTypes.ServiceUnavailable</c>.
/// The compensation contract (the KC user is deleted) is verified on the
/// NSubstitute mock that replaces the registered client.
/// <para>
/// Lives in its own file because the KC client substitution pattern is
/// heavier than the standard happy-path tests in <c>InvitationTests.cs</c> —
/// each test takes a per-test <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// produced by <c>WithWebHostBuilder</c> so the substitute scope doesn't leak
/// into sibling suites.
/// </para>
/// </summary>
[TestClass]
public sealed class InvitationFailureWiringTests : OrganizationIntegrationTestBase
{
    [TestMethod]
    public async Task Invitation_create_502_when_KC_role_assign_fails()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("inv-502-roleassign");
        var inviteeEmail = $"target-{Guid.NewGuid():N}@{adminEmail.Split('@')[1]}";

        // The kcId we make the mock return is the same one whose deletion we
        // verify in the compensation assertion — keeps the test self-consistent
        // without reaching into the DB for the issued id.
        var kcId = Guid.NewGuid();

        // Build a per-test factory that swaps the registered IKeycloakAdminClient
        // for an NSubstitute mock. Fx.WithWebHostBuilder returns a new factory
        // that inherits the parent's ConfigureTestServices wiring (env vars,
        // test JWT signer, Postgres connection) — so we only need to add the
        // KC client substitution here.
        var kc = Substitute.For<IKeycloakAdminClient>();
        kc.CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(kcId);
        kc.AssignRealmRoleAsync(kcId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.Unexpected, "role-assign failed"));

        using var customFactory = Fx.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IKeycloakAdminClient>();
                services.AddSingleton(kc);
            });
        });

        try
        {
            // Mint a token via the shared signer (still valid because
            // ConfigureTestServices in the parent factory wired the signer's
            // public key into JWT validation). GetSubClaimAsync exposes the
            // deterministic sub Guid for `adminEmail` (SubFor is protected on
            // the fixture base, so we route through the public sibling).
            var sub = await Fx.GetSubClaimAsync(adminEmail);
            var token = Fx.Signer.IssueForTenant(
                new Kartova.SharedKernel.Multitenancy.TenantId(tenantId),
                new[] { KartovaRoles.OrgAdmin },
                subject: sub.ToString());
            var client = customFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(inviteeEmail, KartovaRoles.Member));

            // 502 wire mapping pinned to the ServiceUnavailable problem-type so
            // any mutation that swaps the type slug (e.g. to InternalServerError)
            // would fail this assertion.
            Assert.AreEqual(HttpStatusCode.BadGateway, resp.StatusCode);
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(
                ProblemTypes.ServiceUnavailable,
                doc.RootElement.GetProperty("type").GetString());

            // Compensation: the orphan KC user MUST be deleted. NSubstitute
            // counts the calls — the handler runs DeleteUserAsync exactly once
            // when role-assign throws.
            await kc.Received(1).DeleteUserAsync(kcId, Arg.Any<CancellationToken>());

            // DB invariants: no invitation row + no users projection row should
            // have been persisted — the handler returns before SaveChangesAsync.
            await using var db = new OrganizationDbContext(BypassOptions());
            var invitationCount = await db.Invitations
                .CountAsync(i => i.Email == inviteeEmail.ToLowerInvariant());
            Assert.AreEqual(0, invitationCount,
                "Role-assign failure must short-circuit before any DB write.");
            var userCount = await db.Users
                .CountAsync(u => u.Email == inviteeEmail.ToLowerInvariant());
            Assert.AreEqual(0, userCount);
        }
        finally
        {
            // No KC cleanup — the KC client is the NSubstitute mock; no real
            // realm user was provisioned. We only need to drop the org row.
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }
}
