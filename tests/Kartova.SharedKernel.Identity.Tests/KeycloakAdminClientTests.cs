using System.Net;
using System.Text.Json;
using Duende.IdentityModel.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Identity.Tests;

[TestClass]
public sealed class KeycloakAdminClientTests
{
    private static (KeycloakAdminClient client, StubHttpMessageHandler stub) MakeSut(bool captureBodies = false)
    {
        var stub = new StubHttpMessageHandler { CaptureBodies = captureBodies };
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://keycloak:8080") };
        var tokenHttp = new HttpClient(stub) { BaseAddress = new Uri("http://keycloak:8080") };
        var tokenClient = new TokenClient(tokenHttp, new TokenClientOptions
        {
            Address = "http://keycloak:8080/realms/kartova/protocol/openid-connect/token",
            ClientId = "kartova-admin",
            ClientSecret = "test-secret",
        });
        var options = Options.Create(new KeycloakAdminOptions
        {
            BaseUrl = "http://keycloak:8080",
            Realm = "kartova",
            AdminClientId = "kartova-admin",
            AdminClientSecret = "test-secret",
        });
        var client = new KeycloakAdminClient(http, options, tokenClient, NullLogger<KeycloakAdminClient>.Instance);
        return (client, stub);
    }

    private static void EnqueueTokenResponse(StubHttpMessageHandler stub) =>
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"FAKE_TOKEN","expires_in":300,"token_type":"Bearer"}""", System.Text.Encoding.UTF8, "application/json")
        });

    [TestMethod]
    public async Task CreateUserAsync_returns_user_id_from_location_header_on_201()
    {
        var (client, stub) = MakeSut();
        var newId = Guid.NewGuid();
        EnqueueTokenResponse(stub);
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Headers = { Location = new Uri($"http://keycloak:8080/admin/realms/kartova/users/{newId}") }
        });

        var result = await client.CreateUserAsync(
            new CreateKeycloakUserRequest("a@b.c", null, null, Guid.NewGuid().ToString(), ["UPDATE_PASSWORD"]),
            CancellationToken.None);

        Assert.AreEqual(newId, result);
    }

    [TestMethod]
    public async Task CreateUserAsync_throws_EmailAlreadyExists_on_409()
    {
        var (client, stub) = MakeSut();
        EnqueueTokenResponse(stub);
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.Conflict));

        var ex = await Assert.ThrowsExactlyAsync<KeycloakAdminException>(() =>
            client.CreateUserAsync(new CreateKeycloakUserRequest("a@b.c", null, null, Guid.NewGuid().ToString(), []), CancellationToken.None));
        Assert.AreEqual(KeycloakAdminError.EmailAlreadyExists, ex.Error);
    }

    [TestMethod]
    public async Task GetUserAsync_returns_null_on_404()
    {
        var (client, stub) = MakeSut();
        EnqueueTokenResponse(stub);
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetUserAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task DeleteUserAsync_throws_NotFound_on_404()
    {
        var (client, stub) = MakeSut();
        EnqueueTokenResponse(stub);
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsExactlyAsync<KeycloakAdminException>(() =>
            client.DeleteUserAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.AreEqual(KeycloakAdminError.NotFound, ex.Error);
    }

    [TestMethod]
    public async Task CreateUserAsync_sends_username_field_equal_to_email_in_payload()
    {
        // Regression: real KeyCloak rejects the create-user payload with 400 unless either the
        // realm sets registrationEmailAsUsername=true or the payload includes an explicit username.
        // The realm seed does not set the flag, so the payload MUST carry username = email.
        // CaptureBodies = true: this test inspects the JSON sent to the create endpoint, so it
        // opts into the (otherwise off-by-default) body capture on the stub handler.
        var (client, stub) = MakeSut(captureBodies: true);
        var newId = Guid.NewGuid();
        EnqueueTokenResponse(stub);
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Headers = { Location = new Uri($"http://keycloak:8080/admin/realms/kartova/users/{newId}") }
        });

        await client.CreateUserAsync(
            new CreateKeycloakUserRequest("alice@example.com", "Alice", "Example", Guid.NewGuid().ToString(), ["UPDATE_PASSWORD"]),
            CancellationToken.None);

        var bodyJson = stub.CapturedBodies[1];   // [0] is token, [1] is create
        Assert.IsNotNull(bodyJson, "Create-user request must have a body.");
        using var doc = JsonDocument.Parse(bodyJson!);
        Assert.IsTrue(doc.RootElement.TryGetProperty("username", out var usernameProp),
            $"Create-user payload must include 'username' field. Body was: {bodyJson}");
        Assert.AreEqual("alice@example.com", usernameProp.GetString());
        // Sanity-check: email also present and matches.
        Assert.AreEqual("alice@example.com", doc.RootElement.GetProperty("email").GetString());
    }

    [TestMethod]
    public async Task CreateUserAsync_attaches_bearer_token()
    {
        var (client, stub) = MakeSut();
        var newId = Guid.NewGuid();
        EnqueueTokenResponse(stub);
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Headers = { Location = new Uri($"http://keycloak:8080/admin/realms/kartova/users/{newId}") }
        });

        await client.CreateUserAsync(new CreateKeycloakUserRequest("a@b.c", null, null, Guid.NewGuid().ToString(), []), CancellationToken.None);

        var createReq = stub.Captured[1];   // [0] is token, [1] is create
        Assert.AreEqual("Bearer", createReq.Headers.Authorization?.Scheme);
        Assert.AreEqual("FAKE_TOKEN", createReq.Headers.Authorization?.Parameter);
    }

    // ChangeRealmRoleAsync HTTP-flow tests

    [TestMethod]
    public async Task ChangeRealmRoleAsync_happy_path_issues_delete_then_post_assign()
    {
        // Request sequence: [0] token, [1] GET list-roles, [2] DELETE remove-roles,
        //                   [3] token (AssignRealmRoleAsync), [4] GET role-object, [5] POST assign
        var (client, stub) = MakeSut();
        var userId = Guid.NewGuid();

        EnqueueTokenResponse(stub);
        // [1] GET list — returns "Member" as current role
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":"r-member","name":"Member"}]""", System.Text.Encoding.UTF8, "application/json")
        });
        // [2] DELETE remove-roles
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        // [3] token for AssignRealmRoleAsync
        EnqueueTokenResponse(stub);
        // [4] GET role-object for "OrgAdmin"
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"r-oa","name":"OrgAdmin"}""", System.Text.Encoding.UTF8, "application/json")
        });
        // [5] POST assign
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.ChangeRealmRoleAsync(userId, "OrgAdmin", CancellationToken.None);

        // [1] GET list — correct URL and Bearer header
        var listReq = stub.Captured[1];
        Assert.AreEqual(HttpMethod.Get, listReq.Method);
        StringAssert.EndsWith(listReq.RequestUri!.AbsolutePath, $"/users/{userId}/role-mappings/realm");
        Assert.AreEqual("Bearer", listReq.Headers.Authorization?.Scheme);
        Assert.AreEqual("FAKE_TOKEN", listReq.Headers.Authorization?.Parameter);

        // [2] DELETE was issued
        var deleteReq = stub.Captured[2];
        Assert.AreEqual(HttpMethod.Delete, deleteReq.Method);
        StringAssert.EndsWith(deleteReq.RequestUri!.AbsolutePath, $"/users/{userId}/role-mappings/realm");

        // [5] POST assign was issued
        var assignReq = stub.Captured[5];
        Assert.AreEqual(HttpMethod.Post, assignReq.Method);
        StringAssert.EndsWith(assignReq.RequestUri!.AbsolutePath, $"/users/{userId}/role-mappings/realm");
    }

    [TestMethod]
    public async Task ChangeRealmRoleAsync_skips_delete_when_nothing_to_remove()
    {
        // When the current role list already only has the target role (OrgAdmin → OrgAdmin),
        // RolesToRemove returns empty → no DELETE should be issued.
        // Request sequence: [0] token, [1] GET list-roles,
        //                   [2] token (AssignRealmRoleAsync), [3] GET role-object, [4] POST assign
        var (client, stub) = MakeSut();
        var userId = Guid.NewGuid();

        EnqueueTokenResponse(stub);
        // [1] GET list — already has OrgAdmin; nothing to strip
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":"r-oa","name":"OrgAdmin"}]""", System.Text.Encoding.UTF8, "application/json")
        });
        // [2] token for AssignRealmRoleAsync
        EnqueueTokenResponse(stub);
        // [3] GET role-object
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"r-oa","name":"OrgAdmin"}""", System.Text.Encoding.UTF8, "application/json")
        });
        // [4] POST assign
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.ChangeRealmRoleAsync(userId, "OrgAdmin", CancellationToken.None);

        // Total requests: 5 (token, list, token, get-role, assign) — no DELETE
        Assert.AreEqual(5, stub.Captured.Count);
        Assert.IsFalse(stub.Captured.Any(r => r.Method == HttpMethod.Delete),
            "No DELETE should be issued when there is nothing to remove.");

        // POST assign was still issued
        var assignReq = stub.Captured[4];
        Assert.AreEqual(HttpMethod.Post, assignReq.Method);
    }

    [TestMethod]
    public async Task ChangeRealmRoleAsync_throws_NotFound_when_list_roles_returns_404()
    {
        // Request sequence: [0] token, [1] GET list-roles → 404
        var (client, stub) = MakeSut();

        EnqueueTokenResponse(stub);
        stub.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsExactlyAsync<KeycloakAdminException>(() =>
            client.ChangeRealmRoleAsync(Guid.NewGuid(), "OrgAdmin", CancellationToken.None));

        Assert.AreEqual(KeycloakAdminError.NotFound, ex.Error);
    }
}
