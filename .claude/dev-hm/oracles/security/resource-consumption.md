# Security oracle — Resource consumption

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-120 | Endpoint resource limits | New collection-returning endpoints enforce pagination or an explicit result cap, and new request-body handling is subject to a size limit — in the diff or in the platform layer the handoff names | S2 | CWE-770, CWE-400 · API4 | knowledge/security/secure-coding-review/resource-consumption.md |
