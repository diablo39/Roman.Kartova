# Security oracle — Secrets management

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-020 | No hardcoded secrets | Zero credential, API-key, token, or private-key literals in source, committed config, IaC, or test fixtures in the diff; secrets are read from the environment or a secret manager | S0 | A02 · CWE-798 · V13 | knowledge/security/secure-coding-review/secrets.md |
| SEC-021 | Secrets not logged | No secret-bearing value appears in log statements, error messages, exception payloads, or debug output added by the diff | S1 | A09 · CWE-532 · V16 | knowledge/security/secure-coding-review/secrets.md |
| SEC-022 | Secrets not in URLs or tracked files | No secret in query strings, generated docs, or example files; local env files are VCS-ignored; templates use placeholders | S1 | CWE-598, CWE-312 · V13 | knowledge/security/secure-coding-review/secrets.md |
| SEC-023 | Single access path | New code reads secrets through the project's config/secret module; zero new direct environment reads outside it (n/a when the project has no such module) | S2 | V13 | knowledge/security/secure-coding-review/secrets.md |
