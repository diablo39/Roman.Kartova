# Security oracle (SEC-*) — index

Language-agnostic security checks applied to every code change by three roles in sequence:
developer self-check, reviewer verification, security gate. Stack-specific SEC-<code>-NNN entries
live in `oracles/addenda/<stack>.md`.

Diff scope, determinism, verdict grammar and severity tiers come from your own prompt. This file
adds only what is specific to running this oracle:

- IDs are stable and never reused. Where a core entry and a stack-addendum entry cover the same
  defect, the stricter severity governs.
- Standards mapping (OWASP Top 10:2025, CWE Top 25, ASVS 5.0.0, API Top 10, GenAI lists) lives in
  `oracles/security/coverage-map.md`. Read it only when asked which standard an entry maps to —
  never to run the oracle.

## Section routing

Read **only** the section files whose trigger the diff activates.

| Section | File | IDs | Read when the diff touches |
|---|---|---|---|
| Injection | `oracles/security/injection.md` | SEC-001–SEC-005 | SQL/OS-command/HTML/template/LDAP/XPath/NoSQL sinks, regex on external input |
| Authentication and authorization | `oracles/security/authn-authz.md` | SEC-010–SEC-019 | endpoints, handlers, consumers, login, password storage, JWT, OAuth, cookies |
| Secrets management | `oracles/security/secrets.md` | SEC-020, SEC-021, SEC-022, SEC-023 | credential/key/token literals, config, IaC, env reads, log statements |
| Crypto and TLS | `oracles/security/crypto-tls.md` | SEC-030–SEC-035 | hashing, encryption, cert validation, TLS config, random values |
| Input validation and binding | `oracles/security/input-validation.md` | SEC-040–SEC-044 | new external input, request binding/DTOs, uploads, redirects, CSV/header writes |
| Cross-origin and request forgery | `oracles/security/cross-origin-csrf.md` | SEC-046, SEC-047, SEC-048 | cookie-authenticated state changes, CORS config, HTML responses |
| Path traversal | `oracles/security/path-traversal.md` | SEC-045 | filesystem paths built from external input |
| Deserialization | `oracles/security/deserialization.md` | SEC-050, SEC-051, SEC-052 | native deserialization, XML/YAML parsers, archive extraction |
| Supply chain and dependencies | `oracles/security/supply-chain.md` | SEC-060–SEC-065 | manifest/lockfile changes, CI workflow changes, vendored source |
| Error and exception handling | `oracles/security/error-handling.md` | SEC-070, SEC-071, SEC-072, SEC-073 | catch/except blocks, error responses, resource cleanup on security paths |
| Logging and monitoring | `oracles/security/logging.md` | SEC-080, SEC-081, SEC-082, SEC-186 | log statements, security events, log shipping config |
| SSRF | `oracles/security/ssrf.md` | SEC-090, SEC-091 | server-side outbound requests whose target derives from external input |
| Memory safety (native code) | `oracles/security/memory-safety.md` | SEC-100, SEC-101, SEC-102, SEC-103 | C/C++/unsafe code, buffers, pointer lifetimes, size arithmetic |
| Concurrency | `oracles/security/concurrency.md` | SEC-110, SEC-111, SEC-112 | shared mutable state, locks, check-then-act sequences |
| Resource consumption | `oracles/security/resource-consumption.md` | SEC-120 | collection endpoints, request-body handling, unbounded work |
| Control-verification tests | `oracles/security/control-tests.md` | SEC-130–SEC-134 | **always** when the diff adds or alters a protective control |
| Data classification | `oracles/security/data-classification.md` | SEC-140–SEC-146 | new persisted fields, message schemas, or stores carrying personal/payment/health data |
| Transport and peer identity | `oracles/security/transport.md` | SEC-150, SEC-151, SEC-152 | service-to-service calls, TLS negotiation config |
| Session and authorization lifecycle | `oracles/security/session-lifecycle.md` | SEC-160, SEC-161, SEC-162, SEC-163 | long-lived credentials, router/consumer registration, multi-tenant queries |
| Browser client protections | `oracles/security/browser.md` | SEC-170, SEC-171, SEC-185 | HTML-serving apps, browser-side token storage, response cache headers |
| AI feature safeguards | `oracles/security/ai-safeguards.md` | SEC-180–SEC-184 | model calls, prompts, retrieval, model-invocable tools, agent loops |

## Read budget

A typical single-stack feature diff activates **5–8** sections. Opening more than 10 means the
scope statement is wrong — re-read the diff, not the oracle. Stack addenda are read whole; they
are small and every entry is stack-triggered.

Report unopened sections as `n/a` summary counts. Never guess a verdict for a section you did not
open — an unopened section is `n/a`, not `pass`.
