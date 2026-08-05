# Security oracle — Path traversal

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-045 | Path containment | File paths derived from external input are canonicalized and verified to stay inside an allowed base directory before any filesystem operation | S0 | A01 · CWE-22 · V5 | knowledge/security/secure-coding-review/files-and-paths.md |
