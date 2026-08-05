# Secrets and key management — Key separation

Section of `knowledge/security/secrets-and-keys.md`.


One key, one purpose, one environment:

- Per environment: production keys never appear in staging or development configuration, and the
  trust stores differ — a token signed by the staging key is refused by production. The control
  test asserts exactly that refusal, and a config-fixture test asserts no production key IDs
  appear outside production config.
- Per purpose: a signing key is not an encryption key is not a KDF master. Shared-purpose keys
  cannot be rotated independently and invite cross-protocol confusion. Derive purpose-bound
  subkeys instead — `knowledge/security/cryptography-lifecycle.md#key-derivation`.
- Per tenant, where the isolation model warrants it: per-tenant data keys bound the blast radius
  of a key compromise to one tenant and make crypto-shredding per tenant possible
  (`knowledge/security/data-classification.md#retention`).
