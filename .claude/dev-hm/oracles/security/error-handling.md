# Security oracle — Error and exception handling

Section of `oracles/security-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-070 | No swallowed exceptions on security paths | Every catch/except block added on an authentication, authorization, validation, or crypto path either recovers to a defined state or rethrows; zero empty catches on those paths | S1 | A10 · CWE-390 · V16 | knowledge/security/secure-coding-review/exceptions-and-fail-closed-behavior.md |
| SEC-071 | Fail closed | When a security check throws, times out, or errors, the outcome is denial; zero paths where an exception or default value grants access or skips validation | S0 | A10 · CWE-755 · V16 | knowledge/security/secure-coding-review/exceptions-and-fail-closed-behavior.md |
| SEC-072 | No internal detail in external errors | Error responses to external callers contain no stack traces, SQL fragments, file paths, or framework internals; detail goes to server logs keyed by a correlation ID | S2 | A10 · CWE-209 · V16 | knowledge/security/secure-coding-review/exceptions-and-fail-closed-behavior.md |
| SEC-073 | Cleanup on error paths | Resources opened in the diff (files, sockets, connections, locks, temp files) are released on all exit paths via finally/using/defer/RAII | S2 | A10 · CWE-772 | knowledge/security/secure-coding-review/exceptions-and-fail-closed-behavior.md |
