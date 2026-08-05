# Cryptography lifecycle — Weak primitives

Section of `knowledge/security/cryptography-lifecycle.md`.


The constructions our code refuses, and the two controls that make the refusal durable:

- Refused: MD5 and SHA-1 in any security context, DES/3DES/RC4, ECB mode, CBC without
  authentication, RSA PKCS#1 v1.5 encryption, bcrypt or PBKDF2 as the KDF for a new credential
  store (neither is memory-hard; legacy verify-only per
  `knowledge/security/authentication-sessions.md#credential-storage`), compressed-then-encrypted
  secrets, caller-supplied or reused AEAD nonces, security decisions on non-CSPRNG randomness,
  and homegrown token or envelope formats.
- Structural control: the central module simply does not expose these; business code cannot reach
  a weak primitive without importing the raw library directly, which is the reviewable event.
- Static control: a CI pattern scan flags raw crypto-library imports and weak-primitive names
  outside the crypto module; new occurrences fail the build. Review cues live in
  `knowledge/security/secure-coding-review.md`.

Nonce discipline is part of primitive strength: AEAD confidentiality and integrity both fail on
nonce reuse. The module generates nonces internally (random 96-bit with volume bounds, or
counter-based where the store guarantees monotonicity) and does not accept them from callers.
The control test encrypts a sample batch and asserts nonce uniqueness and that the public API
offers no nonce parameter.
