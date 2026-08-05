# API design: contracts, versioning, compatibility, spec artifacts — Compatibility rules

Section of `knowledge/architecture/api-design.md`.


Backward compatible = existing consumers keep working without change. Producer-side rules:

| Safe (additive) | Breaking |
|---|---|
| Add an optional request field with a default | Add a required request field, or make an optional one required |
| Add a response field | Remove or rename any field; change a field's type or format |
| Add an endpoint, subject, or RPC | Change semantics behind an unchanged shape (silent unit change, meaning change) — the worst kind: nothing fails at parse time |
| Add an optional query parameter | Tighten validation on existing inputs (narrower range, stricter pattern) |
| Relax validation (accept more than before) | Reorder/renumber protobuf fields; reuse a deleted field number |
| Add an enum value — only where consumers are known to be tolerant | Add an enum value consumers switch on exhaustively; remove or repurpose one |

Consumer-side (tolerant reader): ignore unknown fields, treat unknown enum values as a handled
"other" case, never depend on field order or on absence of fields. Contract evolution only
works when both sides hold their end; state the tolerant-reader requirement in the contract
docs so it's testable.

Enforce mechanically, not by review vigilance: oasdiff (OpenAPI) or `buf breaking` (protobuf)
as a PR gate against the published contract; consumer-driven contract tests (Pact-style) where
consumers are in-house and enumerable — each consumer publishes the interactions it relies on,
and the provider build fails when one would break.
