# Cryptography lifecycle — No silent downgrade

Section of `knowledge/security/cryptography-lifecycle.md`.


Strong settings must be effective at runtime and non-negotiable by attacker-controlled input:

- Token and envelope readers pin the algorithm allowlist; `none`, unexpected, and retired
  algorithms are refused. The control test presents a token declaring an unexpected algorithm
  (including the none-algorithm and a symmetric-for-asymmetric swap) and asserts refusal.
- Transport never falls back: a failed verification is a refused connection, not a plaintext or
  below-floor retry — test pattern in `knowledge/security/transport-protection.md`.
- Effective-value tests: a test interrogates the running component for its negotiated protocol,
  cipher, and enabled algorithm set and asserts the floor, catching platform or dependency
  changes that weakened defaults without any diff to our configuration files.
- Retired-algorithm reads are refused with a distinct error and emit a security event
  (`knowledge/security/security-logging-detection.md`), so a downgrade attempt is visible, not
  silent, on the read path too.
