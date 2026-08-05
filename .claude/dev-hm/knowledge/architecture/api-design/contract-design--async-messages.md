# API design: contracts, versioning, compatibility, spec artifacts — Contract design — async messages

Section of `knowledge/architecture/api-design.md`.


- The contract is subject/topic name + payload schema + delivery expectations, documented in an
  AsyncAPI file (below). Broker mechanics live in the broker's own documentation.
- Subject hierarchy is API surface: design it for consumer wildcards and document token meaning
  (`orders.v1.created`, `orders.v1.cancelled`); the version token in the subject plays the role
  of the REST path version.
- Events carry facts, not commands, past ownership boundaries; include event id (for
  deduplication), occurrence time, and schema version in the envelope.
- Consumers must tolerate redelivery and unknown fields — at-least-once delivery and rolling
  producers make both routine (`integration-and-data.md`).
