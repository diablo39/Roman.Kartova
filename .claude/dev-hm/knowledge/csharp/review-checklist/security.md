# C# review checklist — Security {#security}

Section of `knowledge/csharp/review-checklist.md`.


| Check | Severity | Notes |
|---|---|---|
| SQL/EF parameterization | S0 | No string-concatenated or interpolated SQL with external input; raw SQL via `FromSqlInterpolated`/parameters. Oracle SEC-CS-001. See {#data-access} |
| No secrets in source | S0 | Connection strings, keys, tokens come from configuration/secret store, not literals. Oracle SEC-CS-002. See {#secrets} |
| Safe deserialization | S0 | No `BinaryFormatter`; no polymorphic JSON with unrestricted type resolution on untrusted input. Oracle SEC-CS-003. See {#deserialization} |
| Command/path/LDAP injection | S0 | External input never concatenated into a process argument, file path, or directory command unvalidated |
| Strong crypto | S1 | No MD5/SHA1 for security, no ECB; `RandomNumberGenerator` (not `System.Random`) for tokens/salts. Oracle SEC-CS-004. See {#crypto} |
| Path traversal | S0 | User paths canonicalized and confined to a base directory before use. Oracle SEC-CS-005 |
| AuthN/AuthZ enforced | S0 | Endpoints require authorization; no `[AllowAnonymous]` on protected routes; server-side authorization, not client-trusted claims. Core SEC-010 |
| Output encoding | S1 | Data rendered into HTML/URL/headers is context-encoded; no raw interpolation into markup |
| TLS/cert validation | S0 | No `ServerCertificateCustomValidationCallback` returning `true`; HTTPS enforced. Oracle SEC-CS-006 |

The core oracle's S0 entries apply in full even where no row above matches — notably fail closed
(SEC-071), password storage via a KDF (SEC-013), and hardcoded secrets (SEC-020); a C#-only walk
still runs the applicable core SEC-*/QUA-* set. See
`knowledge/security/secure-coding-review.md` for the cross-language basis.
