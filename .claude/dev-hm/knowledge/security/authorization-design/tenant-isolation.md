# Authorization design — Tenant isolation

Section of `knowledge/security/authorization-design.md`.


In multi-tenant code, the tenant is a fact about the authenticated context, never a parameter of
the request. A tenant ID in the payload or an unverified header may select presentation; it may
not select data.

Enforcement layers, strongest first — use the strongest the stack offers, and make the weaker
ones structural rather than remembered:

- Store-enforced: the database applies the tenant predicate itself — row-level security keyed on
  a session variable set from the authenticated context, or physically scoped schemas or
  databases per tenant. A query that forgets the tenant returns nothing, regardless of who wrote
  it. Engine specifics live in `knowledge/postgresql/schema-design.md`.
- Repository-enforced: the data-access API requires a tenant context in its type signature — a
  scoped repository constructed per-request from the principal, with no tenant-free query method
  exported. The unsafe call is unrepresentable, not just discouraged.
- Query-predicate discipline: every statement carries the tenant condition from the
  authenticated context. This is the weakest layer because it is per-query and per-developer;
  where it is the only layer, the two-tenant control test below is the safety net that keeps it
  honest.

Context that leaves the request must carry its tenancy: background jobs and queued messages
include the tenant explicitly, and the consumer re-establishes the scoped context before
touching data — a worker processing tenant A's job with an unscoped connection is a cross-tenant
path with no request anywhere in sight. Cross-tenant administrative surfaces are their own
declared, audited exception, gated by step-up verification
(`knowledge/security/authentication-sessions.md#step-up`).

The control test fixture holds two tenants and asserts that no operation — including list
endpoints, search, exports, and error responses — ever surfaces the other tenant's rows.
