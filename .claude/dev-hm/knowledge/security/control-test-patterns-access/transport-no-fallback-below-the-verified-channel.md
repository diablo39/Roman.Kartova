# Control-test patterns: transport, authentication, session, authorization — Transport: no fallback below the verified channel

Section of `knowledge/security/control-test-patterns-access.md`.


Our clients treat a failed verification as final: no retry over plaintext, no renegotiation
below the protocol floor. Verify with a recorder: alongside the unverifiable TLS endpoint, the
fixture binds a plaintext listener where a fallback would land, and asserts it stays silent.

```python
def test_tls_failure_does_not_fall_back_to_plaintext(self_signed_server, plaintext_recorder):
    with pytest.raises(httpx.ConnectError):
        fetch_orders(base_url=self_signed_server.url)
    assert plaintext_recorder.requests_received == 0    # the failure stayed a failure
```

The same recorder technique verifies retry wrappers and resilience layers added around the
client: whatever the retry policy does, it must not vary the transport downward.
