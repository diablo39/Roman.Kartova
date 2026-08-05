# Webhook integrations — Inbound: signature verification

Section of `knowledge/security/webhook-integrations.md`.


The provider signs each delivery; we verify before believing anything in it — including before
parsing the body, since a parser is itself attack surface.

- Scheme: implement exactly the scheme the provider documents. The common shape is HMAC-SHA256
  over the raw body plus a timestamp and delivery identifier, carried in headers; some
  providers sign timestamp-plus-token instead of the body, and some offer asymmetric signatures
  (Ed25519/ECDSA). Where the provider offers both, prefer the asymmetric scheme — verification
  then needs only a public key, and a leaked verifier key signs nothing. The open
  Standard Webhooks convention documents the header and encoding shape most modern providers
  follow.
- Raw bytes: the signature covers the bytes on the wire. Verify against the raw request body
  captured before any framework deserialization; a re-serialized JSON object differs in
  whitespace and key order and fails verification intermittently — the classic bug that gets
  "fixed" by removing verification.
- Constant-time comparison: the computed and received MACs are compared with the platform's
  constant-time equality (SEC-034); ordinary `==` on MAC material is the timing-leak pattern
  the oracle flags.
- Key handling: the signing secret comes through the project's secret path
  (`knowledge/security/secrets-and-keys.md#one-access-path`), one secret per provider per
  environment. Verification accepts a keyset — current plus previous — so provider-side
  rotation is a config change, not an outage
  (`knowledge/security/secrets-and-keys.md#rotation`).
- Failure is refusal: absent, malformed, or non-verifying signatures get the project's refusal
  status with no body detail, and the handler never runs (SEC-071 fail closed). The refusal
  emits its security event with a stable code
  (`knowledge/security/security-logging-detection.md#event-codes`).

```python
def verify_delivery(raw: bytes, headers: Mapping[str, str], keys: list[bytes]) -> None:
    ts, sig = headers["webhook-timestamp"], headers["webhook-signature"]
    require(abs(now() - int(ts)) <= TOLERANCE_SECONDS)            # freshness, see below
    signed = f"{headers['webhook-id']}.{ts}.".encode() + raw      # id and ts inside the MAC
    if not any(hmac.compare_digest(sig, expect(k, signed)) for k in keys):
        raise SignatureError()                                    # refuse before parsing
```
