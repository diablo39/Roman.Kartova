# Secure code review by vulnerability class — Crypto and TLS

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-030 – SEC-035. The review is a table lookup, not cryptanalysis:

| Seen in diff | Verdict |
|---|---|
| MD5/SHA-1 for signatures, tokens, passwords | fail SEC-030 (fine for non-security checksums — verify the use) |
| DES/3DES/RC4, AES in ECB mode | fail SEC-030 |
| AES-GCM / ChaCha20-Poly1305 via a vetted library | pass |
| `verify=False`, trust-all TrustManager, `InsecureSkipVerify`, `NODE_TLS_REJECT_UNAUTHORIZED=0` | fail SEC-031 — S0 even in "temporary" or test-adjacent code importable from production |
| TLS minimum below 1.2 | fail SEC-032 (BCP 195 / RFC 9325: 1.2 floor, prefer 1.3) |
| `random()`/`Math.random`/`rand()` for tokens, IDs, keys | fail SEC-033 — require the platform CSPRNG |
| Hand-rolled cipher, MAC compare, padding, or KDF logic | fail SEC-034 |
| Static or reused IV/nonce | fail SEC-035 — GCM nonce reuse is catastrophic, not degraded |

Constant-time comparison for MACs and tokens (`hmac.compare_digest`, `crypto.timingSafeEqual`)
is part of SEC-034: `==` on secret material is custom crypto by omission.
