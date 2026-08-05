# Authentication and session integrity — Credential storage

Section of `knowledge/security/authentication-sessions.md`.


Where our code verifies passwords, storage follows SEC-013, tiered so a new store has exactly
one safe answer:

- Recommended (safe by default): a memory-hard password KDF — argon2id (preferred) or
  scrypt — through a vetted library.
- Use only if already in use: bcrypt and PBKDF2 verify records in a store that pre-exists the
  change, on the condition that the login path rehashes each verified password to a
  memory-hard KDF (migration below). Neither is memory-hard: bcrypt is CPU-bound with a small
  fixed state and silently truncates input at 72 bytes; PBKDF2 is justified only where a
  FIPS-validated module is mandated, with NIST-scale iteration counts, and the handoff records
  that constraint.
- Not for new work: bcrypt or PBKDF2 for a new credential store, any general-purpose hash
  (salted or not), and any reversible encryption of passwords.

The lifecycle around the chosen KDF:

- Cost parameters come from current library defaults and are reviewed at upgrade; the stored
  format encodes its own parameters, so strengthening applies to new hashes without a flag day.
- Legacy hashes migrate on successful login: verify against the old format, rehash with the
  current one, mark the record.
- Verification failures return one uniform error regardless of whether the account exists, and
  the endpoint carries the anti-automation controls of SEC-015
  (`knowledge/security/api-surface.md`).
