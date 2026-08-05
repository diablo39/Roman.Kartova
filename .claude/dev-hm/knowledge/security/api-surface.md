# API surface protection

The controls that keep our API surface bounded: every request our services accept matches a
declared contract, every collection response has a size ceiling, every caller has a budget, and
every error reveals only what the contract promises. Structured against the OWASP API Security
Top 10 (edition per `knowledge/shared/versions.md`). Each section states the control our code
provides and the test that asserts it holds; the test mandate and marker convention live in
`knowledge/security/control-verification-tests.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| OWASP API Top 10 map | `knowledge/security/api-surface/owasp-api-top-10-map.md` |
| Schema contracts | `knowledge/security/api-surface/schema-contracts.md` |
| Pagination and result caps | `knowledge/security/api-surface/pagination-and-result-caps.md` |
| Rate limits and quotas | `knowledge/security/api-surface/rate-limits-and-quotas.md` |
| Method and content-type restraint | `knowledge/security/api-surface/method-and-content-type-restraint.md` |
| Error shape | `knowledge/security/api-surface/error-shape.md` |
| Inventory | `knowledge/security/api-surface/inventory.md` |
