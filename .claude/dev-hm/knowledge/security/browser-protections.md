# Browser protections

The client-side controls our web code ships: output that renders as data, a content-security
policy that refuses script we did not declare, headers that deny framing and downgrade, a
request-forgery defense on every cookie-authenticated state change, and a deliberate decision
about where tokens live. This file is the cross-stack policy; per-stack implementation depth
lives in `knowledge/typescript/security.md`; runnable verification patterns for every section
are in `knowledge/security/control-test-patterns-browser.md`, under the mandate in
`knowledge/security/control-verification-tests.md`. The server-surface counterparts — CORS
restraint, content-type restraint, error shape — live in `knowledge/security/api-surface.md`
and the cross-origin section of `knowledge/security/secure-coding-review.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Safe rendering | `knowledge/security/browser-protections/safe-rendering.md` |
| CSP | `knowledge/security/browser-protections/csp.md` |
| Browser protection headers | `knowledge/security/browser-protections/browser-protection-headers.md` |
| Response cache hygiene | `knowledge/security/browser-protections/response-cache-hygiene.md` |
| Request-forgery defenses | `knowledge/security/browser-protections/request-forgery-defenses.md` |
| Token storage | `knowledge/security/browser-protections/token-storage.md` |
| Subresource integrity | `knowledge/security/browser-protections/subresource-integrity.md` |
