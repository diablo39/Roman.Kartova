# C# review checklist — Crypto {#crypto}

Section of `knowledge/csharp/review-checklist.md`.


Use current algorithms: SHA-256+ for hashing, AES-GCM for symmetric encryption, ASP.NET Core Data
Protection for app secrets. Generate salts, tokens, and keys with `RandomNumberGenerator`.

Password storage (core oracle SEC-013, S0) uses a memory-hard KDF — never a bare hash, salted or
not, and never reversible encryption:

- Recommended (safe by default): Argon2id through a vetted library (preferred), or scrypt.
- Use only if already in use: the ASP.NET Core Identity `PasswordHasher` (PBKDF2) is acceptable
  only for existing ASP.NET Identity credential stores on current framework defaults (format V3,
  current iteration count), or where a FIPS-validated module is mandated — record the constraint
  in the handoff. New work uses argon2id/scrypt.
- Not for new work: PBKDF2 or bcrypt for a new credential store; neither is memory-hard.
