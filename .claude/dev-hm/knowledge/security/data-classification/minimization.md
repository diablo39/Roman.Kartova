# Data classification — Minimization

Section of `knowledge/security/data-classification.md`.


The least data is the cheapest control: what we never collect we never need to encrypt, mask,
retain, or report breached.

- Every newly collected Tier-3 field has a recorded purpose (a handoff line suffices); no
  speculative collection because a field might be useful later.
- No fan-out by default: Tier-3 data does not propagate into caches, search indexes,
  denormalized copies, or analytics stores unless the purpose travels with it. Prefer tokenized
  references — a payment token, an opaque subject ID — resolvable only by the owning service, so
  downstream systems hold pointers, not the data.
- Response allowlists: API responses, events, and exports serialize enumerated fields (DTO or
  serializer allowlist), never whole entities. A newly added sensitive column then stays
  internal by default instead of leaking by default.
- Third parties get the minimum: payloads to processors and vendors carry the fields the
  integration needs, assembled explicitly, not the source object.

Verification: a contract test pins the exact serialized shape of responses and events; a field
added without review fails the test. The collection-purpose rule is a review control, checked at
the gate against the handoff.
