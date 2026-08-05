# Authorization design

The controls that keep every protected operation checked. The diff-review quick reference lives
in `knowledge/security/secure-coding-review/access-control.md` (SEC-010 – SEC-012); this file
covers the design behind those entries — how deny-by-default gets wired so it cannot be
forgotten, where decisions are computed, and how ownership and tenancy stay attached to every
data access. Runnable test patterns — wrong role refused, another principal's object refused —
are in `knowledge/security/control-test-patterns-access.md`; the mandate binding controls to
tests is `knowledge/security/control-verification-tests.md`. Authentication establishes who is
calling (`knowledge/security/authentication-sessions.md`); authorization decides whether this
caller may do this to this resource. They are separate layers, and both run on every protected
operation.

The design goal throughout: make the safe path the structural default, so that a developer who
forgets authorization ships a refused request, not an open one.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Deny by default | `knowledge/security/authorization-design/deny-by-default.md` |
| Checks at the handler | `knowledge/security/authorization-design/checks-at-the-handler.md` |
| Object-level ownership | `knowledge/security/authorization-design/object-level-ownership.md` |
| Tenant isolation | `knowledge/security/authorization-design/tenant-isolation.md` |
| Centralized policy | `knowledge/security/authorization-design/centralized-policy.md` |
| Verification tests | `knowledge/security/authorization-design/verification-tests.md` |
