# Security oracle — Crypto and TLS

Section of `oracles/security-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-030 | Approved algorithms | No MD5 or SHA-1 for any security purpose; no DES/3DES/RC4; no ECB mode; symmetric encryption uses an AEAD (AES-GCM or ChaCha20-Poly1305) | S1 | A04 · CWE-327 · V11 | knowledge/security/secure-coding-review/crypto-and-tls.md |
| SEC-031 | Certificate validation on | Zero code paths that disable certificate or hostname verification (verify=false, trust-all managers, insecure flags), including test helpers importable from production code | S0 | A04 · CWE-295 · V12 | knowledge/security/secure-coding-review/crypto-and-tls.md |
| SEC-032 | TLS floor | TLS configuration in the diff sets minimum TLS 1.2 and offers 1.3 (BCP 195 / RFC 9325); no plaintext transport for credential-bearing traffic | S1 | A04 · CWE-319 · V12 | knowledge/security/secure-coding-review/crypto-and-tls.md |
| SEC-033 | CSPRNG for security values | Tokens, session IDs, nonces, and keys are generated with the platform CSPRNG; zero general-purpose PRNG (rand, Math.random) uses for security values | S1 | A04 · CWE-338 · V11 | knowledge/security/secure-coding-review/crypto-and-tls.md |
| SEC-034 | No custom crypto | Primitives and protocols come from vetted libraries; the diff implements no cipher, MAC, padding, or key-derivation logic of its own; comparisons of secrets, MACs, or signatures against external input use a constant-time equality function, not ordinary equality operators | S1 | A04 · CWE-327, CWE-208 · V11 | knowledge/security/secure-coding-review/crypto-and-tls.md |
| SEC-035 | Nonce and IV discipline | Every encryption call uses a fresh unique IV/nonce; zero static, hardcoded, or counter-reset IVs | S1 | A04 · CWE-329 · V11 | knowledge/security/secure-coding-review/crypto-and-tls.md |
