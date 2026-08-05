# API design: contracts, versioning, compatibility, spec artifacts — Contract design — REST

Section of `knowledge/architecture/api-design.md`.


- Model resources (nouns) and use HTTP method semantics as standardized (current HTTP semantics
  RFC per `knowledge/shared/versions.md`): GET safe and cacheable, PUT/DELETE idempotent, POST
  for creation and non-idempotent actions. An RPC-ish action that fits no resource verb becomes
  a sub-resource (`POST /orders/{id}/cancellation`) rather than a verb-in-URL.
- Errors: one machine-readable error shape everywhere — Problem Details (`application/problem+json`,
  RFC 9457) with `type` as the stable error identifier consumers switch on; never switch on the
  human-readable message.
- Pagination from day one on every collection (cursor-based by default — offset pagination
  degrades and shifts under concurrent writes); filtering and sorting as documented query
  parameters, rejecting unknown ones explicitly or documenting that they're ignored.
- Unsafe retries: accept an `Idempotency-Key` header on POST (IETF draft — cite as convention,
  per `knowledge/shared/versions.md`), store the first response, replay it on key reuse.
- Concurrency: `ETag` + `If-Match` for lost-update protection on PUT/PATCH.
- Long-running operations: `202 Accepted` plus a pollable status resource; don't hold requests
  open past gateway timeout budgets.
