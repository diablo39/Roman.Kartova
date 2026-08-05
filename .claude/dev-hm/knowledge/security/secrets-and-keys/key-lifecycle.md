# Secrets and key management — Key lifecycle

Section of `knowledge/security/secrets-and-keys.md`.


Every cryptographic key has an owner, a single purpose, and a recorded cryptoperiod (NIST
SP 800-57 is the reference vocabulary). The stages our code and configuration must support:

1. Generation — platform CSPRNG at full required length (SEC-033); generated inside the KMS/HSM
   where the key will live, so the private material never transits a developer machine.
2. Distribution — via the secret store or KMS grant, never chat, email, or tickets.
3. Active use — the key referenced by ID, one purpose only (see key separation below).
4. Rotation — a new key version becomes primary for new operations.
5. Retirement — the old version kept for verify/decrypt only; refuses new signing/encryption.
6. Destruction — after the last artifact under the old key is re-encrypted or expired.

The enabling design decision: every ciphertext, token, and signature carries the ID of the key
that produced it (a `kid` header, a version prefix on the blob). Without key IDs, rotation means
trial-decryption or a flag day; with them, it is routine.
