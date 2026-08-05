# Security oracle — Injection

Section of `oracles/security-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-001 | SQL injection | All SQL touching external input uses parameter binding or a bind-API query builder; zero SQL strings built by concatenation or interpolation containing external input | S0 | A05 · CWE-89 (#2, Top 25 2025) · V1 | knowledge/security/secure-coding-review/injection.md |
| SEC-002 | OS command injection | Process invocation uses argument arrays with shell interpretation disabled; zero shell command strings containing external input | S0 | A05 · CWE-78, CWE-77 · V1 | knowledge/security/secure-coding-review/injection.md |
| SEC-003 | Cross-site scripting | Dynamic values rendered into HTML/JS/attribute/URL contexts go through framework auto-escaping or a context-matched encoder; zero raw-HTML sinks (innerHTML, dangerouslySetInnerHTML, v-html) receiving external input without a sanitizer | S0 | A05 · CWE-79 (#1, Top 25 2025) · V1, V3 | knowledge/security/secure-coding-review/output-encoding-and-xss.md |
| SEC-004 | Code and template injection | No eval/exec/Function-style dynamic code execution on external input; template engines receive external input only as data parameters, never as template source | S0 | A05 · CWE-94 · V1 | knowledge/security/secure-coding-review/injection.md |
| SEC-005 | Other interpreters | LDAP/XPath/NoSQL queries built with binding or escaping APIs, zero string-built filters with external input; regexes applied to external input contain no nested unbounded quantifiers | S1 | A05 · CWE-90, CWE-1333 · V1 | knowledge/security/secure-coding-review/injection.md |
