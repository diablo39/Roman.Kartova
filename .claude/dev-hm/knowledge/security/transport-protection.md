# Transport protection

The controls that keep our connections private and our peers verified. The diff-review quick
reference lives in `knowledge/security/secure-coding-review/crypto-and-tls.md` (SEC-030 –
SEC-035); this file covers the policy behind those entries — where the protocol floor sits, how
peers prove their identity to our code, and why a failed verification must stay a failure.
Runnable test patterns for every control here are in
`knowledge/security/control-test-patterns-access.md`; the mandate binding controls to tests is
`knowledge/security/control-verification-tests.md`. Algorithm choice, key derivation, and the
post-quantum transition live in `knowledge/security/cryptography-lifecycle.md`.

Transport protection is two guarantees delivered together: confidentiality and integrity of the
bytes in motion, and the verified identity of the peer at the other end. A connection that
encrypts to an unverified peer delivers the first and silently drops the second — most transport
weaknesses in application code are identity failures, not cipher failures. Every control below
exists to keep both guarantees attached to every connection our code opens or accepts.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Protocol floor and policy | `knowledge/security/transport-protection/protocol-floor-and-policy.md` |
| Certificate and hostname validation | `knowledge/security/transport-protection/certificate-and-hostname-validation.md` |
| Downgrade refusal | `knowledge/security/transport-protection/downgrade-refusal.md` |
| Peer identity | `knowledge/security/transport-protection/peer-identity.md` |
| Post-quantum compatible negotiation | `knowledge/security/transport-protection/post-quantum-compatible-negotiation.md` |
| Verification tests | `knowledge/security/transport-protection/verification-tests.md` |
