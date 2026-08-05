# API compatibility and versioning — Compatibility direction

Section of `knowledge/quality/api-compatibility.md`.


- Backward compatibility: the new provider serves old consumers. The default obligation for
  servers and event producers.
- Forward compatibility: old consumers survive messages from the new producer. The extra
  obligation of event streams and message queues, where consumers upgrade on their own
  schedule — consumers must ignore unknown fields and route unknown event types to a dead-letter
  or log, not crash.
- Both directions at once during every rolling deploy: version N and N-1 of the same service
  run side by side, reading the same database and queues. Every contract change must therefore
  be compatible with the immediately previous release even when both sides are yours — the same
  reasoning as expand-contract for schemas
  (`knowledge/quality/data-migration-safety.md#expand-contract`), and the
  reason rollback stays possible
  (`knowledge/quality/release-readiness.md#rollback`).
