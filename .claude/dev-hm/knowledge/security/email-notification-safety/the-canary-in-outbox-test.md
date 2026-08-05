# Email and notification safety — The canary-in-outbox test

Section of `knowledge/security/email-notification-safety.md`.


The messaging counterpart of the telemetry canary
(`knowledge/security/control-test-patterns-dataflow.md#telemetry-planted-canaries-never-surface-in-captured-output`).
The test harness replaces the provider transport with a capturing outbox — the real mailer
module runs, the wire call is recorded — and the suite asserts over everything captured:

```python
@pytest.mark.control
def test_outbox_carries_no_secret_and_links_ignore_request_host(outbox, client):
    canary = "canary-tok-91d4"
    with planted_reset_token(canary):
        client.post("/password-reset", data={"email": USER},
                    headers={"Host": "attacker.example", "X-Forwarded-Host": "attacker.example"})
    assert len(outbox) == 1                                  # exactly the intended send
    mail = outbox[0]
    assert USER == mail.to_addr and not mail.bcc             # no injected recipients
    assert all(u.startswith(settings.public_base_url) for u in links_in(mail))
    assert "attacker.example" not in mail.raw                # host header did not relocate links
    assert canary not in caplog.text                         # token in mail, never in telemetry
```

The same outbox fixture carries the header-injection and encoding assertions below. Because the
outbox captures the rendered wire form, it catches template regressions no unit test of the
data layer would see.
