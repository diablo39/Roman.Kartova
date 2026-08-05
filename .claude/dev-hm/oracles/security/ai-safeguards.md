# Security oracle — AI feature safeguards

Section of `oracles/security-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-180 | Untrusted content isolation | Untrusted content (user input, retrieved documents, tool results) passed to a model is structurally separated from system instructions (message roles, a template contract that forbids instruction mixing); zero string concatenation of untrusted content into instruction text | S1 | LLM01 · CWE-1427 · V1 | knowledge/security/llm-feature-controls/content-isolation.md |
| SEC-181 | Model output treated as untrusted input | Model output our code consumes is schema-validated before parsing, context-encoded before rendering (SEC-003 applies), and never executed directly; zero raw model output reaching an interpreter, shell, or raw-HTML sink | S1 | LLM05 · CWE-79, CWE-94 · V1 | knowledge/security/llm-feature-controls/output-handling.md |
| SEC-182 | Tool authority bound to caller | Every model-invocable tool enforces the requesting user's authentication and authorization server-side inside the tool implementation, and schema-validates its inputs like any external input; zero tools executing with ambient privileges beyond the caller's | S0 | LLM06 · ASI03 · CWE-862 · V8 | knowledge/security/agentic-tool-controls/authority-binding.md |
| SEC-183 | Irreversible actions gated | Model-initiated actions that are destructive or irreversible (delete, send, pay, deploy) require an explicit confirmation step or run against a declared allowlist; the boundary is stated in the handoff, and a control test asserts the ungated path is refused | S1 | LLM06 · ASI09 · CWE-862 | knowledge/security/agentic-tool-controls/action-gating.md |
| SEC-184 | Stable security event codes | New security events added by the diff emit a stable event type or code alongside the message so detection rules can match them; codes documented where the repo documents them | S2 | A09 · CWE-778 · V16 | knowledge/security/security-logging-detection.md#event-codes |
