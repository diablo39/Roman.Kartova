# TypeScript / web security patterns — Credential and secret comparison {#credentials}

Section of `knowledge/typescript/security.md`.


Cross-language crypto choices live in `knowledge/security/secure-coding-review/crypto-and-tls.md`;
the Node-specific rules are:

- Hash passwords with a memory-hard KDF (core oracle SEC-013, S0).
  - Recommended (safe by default): argon2id via the `argon2` package (preferred), or scrypt via
    `node:crypto`'s `scrypt`/`scryptSync` — available dependency-free in the standard library.
  - Use only if already in use: bcrypt, solely to verify a pre-existing bcrypt credential store
    while rehashing to argon2id or scrypt on successful login. bcrypt is not memory-hard (it runs
    in a fixed ~4 KB state) and silently truncates input at 72 bytes.
  - Not for new work: bcrypt or PBKDF2 for a new credential store. A general-purpose digest
    (SHA-256, MD5) is never a password hash, salted or not.
- Compare secrets, tokens, and signatures with `crypto.timingSafeEqual`, never `===`/`==`, so the
  comparison cannot leak length or content through timing.
- Generate tokens, salts, and ids with `crypto.randomBytes`/`crypto.randomUUID`, not `Math.random`.
