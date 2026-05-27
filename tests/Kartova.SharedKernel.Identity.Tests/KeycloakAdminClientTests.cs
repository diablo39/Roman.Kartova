using System.Net;
using Duende.IdentityModel.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Identity.Tests;

[TestClass]
public sealed class KeycloakAdminClientTests
{
    private static (KeycloakAdminClient client, StubHttpMessageHandler stub) MakeSut()
    {
        var stub = new StubHttpMessageHandler();
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
}
