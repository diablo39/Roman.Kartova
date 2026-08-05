# Security oracle — standards coverage map

Reporting metadata. Not needed to run the oracle; read only when asked which standard an entry maps to.

Coverage map — where each OWASP Top 10:2025 category is enforced:

| OWASP Top 10:2025 | Enforced by |
|---|---|
| A01 Broken Access Control | SEC-011, SEC-012, SEC-044, SEC-045, SEC-046, SEC-090, SEC-162, SEC-163, SEC-182 |
| A02 Security Misconfiguration | SEC-020, SEC-047, SEC-048, SEC-051, SEC-170 |
| A03 Software Supply Chain Failures | SEC-060 – SEC-065 |
| A04 Cryptographic Failures | SEC-030 – SEC-035, SEC-141, SEC-151, SEC-152 |
| A05 Injection | SEC-001 – SEC-005, SEC-180, SEC-181 |
| A06 Insecure Design | Not oracle-checkable; addressed at design time via `knowledge/security/threat-modeling.md` |
| A07 Authentication Failures | SEC-010, SEC-013 – SEC-019, SEC-150, SEC-160, SEC-161, SEC-171 |
| A08 Software or Data Integrity Failures | SEC-050, SEC-063 |
| A09 Security Logging and Alerting Failures | SEC-021, SEC-080 – SEC-082, SEC-142, SEC-184, SEC-186 |
| A10 Mishandling of Exceptional Conditions | SEC-070 – SEC-073 |

Beyond the Top 10 categories: control-verification tests (SEC-130 – SEC-134) cut across every
category — they verify that the controls above keep holding after future changes. Data
classification (SEC-140 – SEC-146) enforces ASVS V14 data protection. The OWASP GenAI lists are
enforced by SEC-180 – SEC-184; the per-risk maps live in
`knowledge/security/llm-feature-controls/llm-top-10-map.md` and
`knowledge/security/agentic-tool-controls/agentic-top-10-map.md`.

