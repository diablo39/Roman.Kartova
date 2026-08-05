# Security oracle — SSRF

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-090 | Outbound URL control | Server-side requests whose target derives from external input validate scheme and host against an allowlist; link-local and metadata addresses (169.254.169.254) and private ranges are blocked unless explicitly intended; redirect targets are re-validated | S1 | A01 · CWE-918 · API7 | knowledge/security/secure-coding-review/ssrf.md |
| SEC-091 | Parsed-host comparison | URL validation compares the parsed host component; zero substring or prefix checks on the raw URL string | S2 | CWE-918, CWE-20 | knowledge/security/secure-coding-review/ssrf.md |
