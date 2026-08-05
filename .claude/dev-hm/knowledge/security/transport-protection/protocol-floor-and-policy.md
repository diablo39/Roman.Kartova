# Transport protection — Protocol floor and policy

Section of `knowledge/security/transport-protection.md`.


Our code sets a protocol floor of TLS 1.2 and offers TLS 1.3, per the deployment guidance
anchored in `knowledge/shared/versions.md` (BCP 195 / RFC 9325). QUIC and HTTP/3 carry TLS 1.3
by construction. In practice the floor control is mostly restraint: current runtimes and
libraries already default to this policy, so the protective work is not lowering it.

- No explicit enablement of protocol versions below the floor, anywhere — including "temporary"
  compatibility settings for a legacy peer. A peer that cannot meet the floor gets an upgrade
  conversation, not a weakened client.
- No hand-copied cipher-suite strings. Library defaults track current guidance; a pinned list
  from an old runbook freezes yesterday's policy and blocks tomorrow's. When compliance requires
  an explicit configuration, set the floor and preferred version, leave suite selection to the
  library within that floor, and record where the configuration lives.
- No plaintext transport for credential-bearing or otherwise non-public traffic. Internal
  traffic is in scope: the zero-trust posture (NIST SP 800-207) treats the internal network as
  untrusted transit, so "it never leaves the cluster" does not exempt a connection.

```python
# fail: floor lowered for one legacy integration — every connection from this context inherits it
ctx.minimum_version = ssl.TLSVersion.TLSv1     # below the floor

# pass: floor pinned, everything above it negotiated by the library
ctx = ssl.create_default_context()             # verifying defaults
ctx.minimum_version = ssl.TLSVersion.TLSv1_2
```

The verification is a configuration-fixture test plus a behavior test: the fixture asserts no
setting in the diff names a protocol below the floor, and a control test connects our client to
a fixture endpoint offering only a below-floor protocol and asserts the handshake is refused.
