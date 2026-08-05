# Control-test patterns: parameterized statements, encoding, parsing, telemetry, limits — Telemetry: planted canaries never surface in captured output

Section of `knowledge/security/control-test-patterns-dataflow.md`.


Our logging and error layer keeps secret and regulated values out of everything it emits
(tiers and masking: `knowledge/security/data-classification.md`). The test plants a canary — a
unique literal standing in for the secret — drives the operation through the real pipeline, and
asserts the canary appears nowhere captured: log records, error responses, trace attributes.

```python
@pytest.mark.control
def test_failed_login_surfaces_no_credential(client, caplog):
    canary = "canary-3f9d1a"
    r = client.post("/login", data={"user": "x", "password": canary})
    assert r.status_code == 401
    assert canary not in caplog.text     # full records through the real pipeline
    assert canary not in r.text          # nor the error payload
```

```csharp
[TestMethod]
[TestCategory("Control")]
public async Task FailedPayment_SurfacesNoCardNumber()
{
    const string canary = "canary-4999-0001";
    var response = await _client.PostAsJsonAsync("/api/payments", new { card = canary });
    Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    var logged = string.Join("\n", _logs.Collector.GetSnapshot().Select(r => r.Message));
    Assert.DoesNotContain(canary, logged);
    Assert.DoesNotContain(canary, await response.Content.ReadAsStringAsync());
}
```

| Ecosystem | Capture fixture |
|---|---|
| TypeScript | pino/winston test stream or transport attached in the fixture |
| Python | pytest `caplog`; captured stdout for structured JSON logs |
| Java | Logback `ListAppender` attached to the root logger |
| C# | the fake logger collector from the logging test package |
| Rust | a capturing `tracing` subscriber layer installed for the test |
| C++ | an spdlog test sink registered by the fixture |
| Flutter / Dart | a recording listener on the app's `Logger` stream |

Drive the unhandled-failure path deliberately — a dependency error while the canary is in
scope — because exception text is where values slip past field-level masking. Assert against
the fully rendered record (message, structured fields, exception text), not the message alone.
