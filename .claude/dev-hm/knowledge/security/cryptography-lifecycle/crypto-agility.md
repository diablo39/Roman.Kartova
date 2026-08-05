# Cryptography lifecycle — Crypto agility

Section of `knowledge/security/cryptography-lifecycle.md`.


Agility is the control that makes every other choice replaceable before it is broken:

- Inventory: because all use flows through the central module, the module's configuration is the
  algorithm inventory — reviewable, diffable, and the input to any transition plan. Keep it as an
  explicit table (a minimal cryptographic bill of materials), not tribal knowledge.
- Self-describing artifacts: every ciphertext, signature, and token carries its algorithm
  identifier and key ID, the same design that makes key rotation routine in
  `knowledge/security/secrets-and-keys.md#rotation`.
- Readers hold an allowlist: verification and decryption accept the configured set of expected
  algorithms — never "whatever the artifact's header claims".
- Migration is the rotation playbook at the algorithm level: add the new algorithm to the accept
  set, switch writes to it, re-encrypt or re-sign lazily or in batch, then remove the old
  algorithm from the accept set.

Verification: the dual-accept round trip — artifacts written under the old algorithm still read;
new writes carry the new identifier; after retirement, old-algorithm artifacts are refused.
