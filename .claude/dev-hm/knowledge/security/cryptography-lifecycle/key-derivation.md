# Cryptography lifecycle — Key derivation

Section of `knowledge/security/cryptography-lifecycle.md`.


One master key, many purpose-bound subkeys, derived with HKDF and a distinct context string per
purpose (`"v1:invoice-field-encryption"`, `"v1:webhook-signing"`):

- Purposes cannot be confused: a subkey derived for signing never encrypts, which enforces the
  one-key-one-purpose rule from `knowledge/security/secrets-and-keys.md#key-separation` without
  an inventory explosion.
- Per-tenant or per-subject derivation gives isolation and crypto-shredding leverage: destroy or
  rotate one derivation input and one tenant's data is unreadable, nobody else's touched
  (`knowledge/security/data-classification.md#retention`).
- Context strings are versioned so a derivation change is a new context, not a silent
  reinterpretation of the old one.
- Human passwords are not key material for HKDF; they go through a memory-hard password KDF —
  argon2id (preferred) or scrypt — first
  (`knowledge/security/authentication-sessions.md#credential-storage`). Keys are never derived
  by plain hashing or truncating other keys.

Verification: subkeys for different contexts differ; the same context reproduces the same key;
an artifact encrypted under context A refuses to decrypt under context B (the associated data or
context binding asserts it).
