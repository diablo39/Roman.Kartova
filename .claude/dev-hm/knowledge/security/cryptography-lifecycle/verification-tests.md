# Cryptography lifecycle — Verification tests

Section of `knowledge/security/cryptography-lifecycle.md`.


The control tests this file's controls map to, per
`knowledge/security/control-verification-tests.md`:

- Effective settings: negotiated transport floor and the central module's enabled algorithm set
  asserted against the running component, not the config file.
- Weak refusal: artifacts and tokens declaring retired, unexpected, or none algorithms refused.
- At-rest behavior: synthetic plaintext marker absent from raw storage bytes for field- and
  application-layer encryption; infrastructure assertion for storage-level.
- Nonce discipline: batch uniqueness asserted; no caller-supplied nonce path exists.
- Derivation binding: cross-context decryption refused; same-context derivation reproducible.
- Agility round trip: dual-accept during migration; old-algorithm refusal after retirement.
- Post-quantum negotiation: no classical-only group pinning; hybrid handshake asserted where
  testable; attestation-gated key release refused for unattested environments where TEEs are in
  scope.
