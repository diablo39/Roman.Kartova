# Cryptography lifecycle — Algorithm selection

Section of `knowledge/security/cryptography-lifecycle.md`.


Our code never assembles primitives by hand. One central crypto module exposes named operations
(`encrypt_field`, `sign_token`, `derive_subkey`) built on the platform's vetted library through
its misuse-resistant API; algorithm choice is configuration in that one module, which is what
makes the inventory and every later migration a one-file question.

| Job | Default construction | Notes |
|---|---|---|
| Symmetric encryption | AEAD only: AES-256-GCM or ChaCha20-Poly1305 | unique nonce per key and message; associated data binds context |
| General-purpose hashing | SHA-256/SHA-384 or SHA-3 family | not for passwords |
| Password verification | memory-hard password KDF: argon2id (preferred) or scrypt | bcrypt/PBKDF2 are legacy verify-only — `knowledge/security/authentication-sessions.md#credential-storage` |
| Message authentication | HMAC-SHA-256 | constant-time comparison on verify |
| Signatures | Ed25519 or ECDSA P-256 | algorithm-agile format for PQC readiness (see below) |
| Key exchange | X25519; hybrid with ML-KEM where the platform offers it | prefer platform TLS defaults |
| Randomness for security decisions | platform CSPRNG | never a general-purpose PRNG |

NIST SP 800-57 supplies the key-strength vocabulary. NIST's transition roadmap (IR 8547)
deprecates the quantum-vulnerable classical algorithms — RSA, ECDSA, ECDH, finite-field DH — by
2030 and disallows them after 2035; new designs choose with that horizon in mind.
