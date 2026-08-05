# Security oracle — Logging and monitoring

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-080 | Security events logged | New authentication attempts, authorization denials, and validation rejections at trust boundaries emit a log entry with actor, action, outcome, and timestamp through the project logger | S2 | A09 · CWE-778 · V16 | knowledge/security/secure-coding-review/logging.md |
| SEC-081 | Log injection neutralized | External input written to logs goes through structured fields, or has CR/LF and control characters stripped | S2 | A09 · CWE-117 · V16 | knowledge/security/secure-coding-review/logging.md |
| SEC-082 | No sensitive data in logs | No PII, credentials, tokens, or payment data written to logs by the diff; identifiers appear masked or truncated | S1 | A09 · CWE-532 · V14 | knowledge/security/secure-coding-review/logging.md |
| SEC-186 | Tamper-evident log shipping | Security-event shipping configuration added or changed by the diff sends events to a store the emitting application cannot rewrite: the shipping principal's credentials permit append/write only — no update, delete, or retention-shortening rights — or the destination's append-only/immutability feature is enabled and cited in the handoff; zero security-log destinations the emitting service can erase | S2 | A09 · CWE-778 · V16 | knowledge/security/security-logging-detection.md#tamper-evidence |
