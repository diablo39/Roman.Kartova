# Control-test patterns: transport, authentication, session, authorization — Transport: an unverifiable peer is a refused connection

Section of `knowledge/security/control-test-patterns-access.md`.


Our transport clients keep certificate and hostname verification enabled; when the peer cannot
be verified, the result is a refused connection with a verification error — never a request.
The pattern: the fixture starts a local TLS endpoint whose certificate our client's trust store
does not include; our production client code connects; the test asserts the verification error
and that zero requests reached the endpoint.

```python
def test_client_refuses_unverifiable_peer(self_signed_server):
    with pytest.raises(httpx.ConnectError) as e:
        fetch_orders(base_url=self_signed_server.url)   # our production client code
    assert "CERTIFICATE_VERIFY_FAILED" in str(e.value)
    assert self_signed_server.requests_received == 0
```

```csharp
[TestMethod]
public async Task Client_RefusesUnverifiablePeer()
{
    using var server = SelfSignedTlsServer.Start();
    var client = OrdersClient.Create(server.Url);       // production factory, default handler
    await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetOrdersAsync());
    Assert.AreEqual(0, server.RequestsReceived);
}
```

```dart
test('control: client refuses unverifiable peer', () async {
  final server = await startSelfSignedServer();
  final api = OrdersApi(baseUrl: server.url);           // no badCertificateCallback registered
  await expectLater(api.fetchOrders(), throwsA(isA<HandshakeException>()));
});
```

Expected refusal shape when the client is left at its verifying defaults:

| Ecosystem | Refusal to assert |
|---|---|
| TypeScript (Node fetch/undici) | rejected promise whose cause code names the unverifiable certificate |
| Python (httpx/requests) | `ConnectError`/`SSLError` naming `CERTIFICATE_VERIFY_FAILED` |
| Java (`java.net.http.HttpClient`) | `SSLHandshakeException` |
| C# (`HttpClient`) | `HttpRequestException` with an inner `AuthenticationException` |
| Rust (reqwest) | connect error whose message names the certificate |
| C++ (libcurl) | `CURLE_PEER_FAILED_VERIFICATION`, with verify options left at defaults |
| Flutter / Dart (`HttpClient`/dio) | `HandshakeException` |

This test is the regression tripwire for verification being switched off anywhere in the client
construction path: disabling verification makes the connection succeed and the test fail —
exactly the fails-when-removed property.
