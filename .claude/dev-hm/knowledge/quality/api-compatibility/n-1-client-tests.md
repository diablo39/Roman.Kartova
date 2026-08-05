# API compatibility and versioning — N-1 client tests

Section of `knowledge/quality/api-compatibility.md`.


A provider's own suite tests the contract as of HEAD — it cannot see that yesterday's deployed
consumer breaks. Compatibility needs tests pinned to what is released, not to what is current:

- Consumer-driven contracts, held at deployed versions: the broker-based Pact workflow verifies
  the provider against the contract of each consumer version currently deployed (and each about
  to deploy), not just the newest — the can-i-deploy question. That is the general N-1 test.
- Schema snapshots: keep the last released schema (OpenAPI/protobuf/Avro) as a committed
  artifact or registry entry and diff every change against it with a breaking-change linter.
  Cheap, catches the whole structural taxonomy above, misses the semantic rows.
- Round-trip tests for events: serialize with the new schema, deserialize with the previous
  release's schema (and the reverse) to prove both compatibility directions across one version
  step.
- Rollback compatibility is N-1 in reverse and belongs to the same suite: the previous release
  must parse what the new release wrote — data outlives code
  (`knowledge/quality/release-readiness.md#rollback`).
- Long-lived clients (mobile apps, desktop installs, partner SDKs) stretch N-1 to N-k: the
  supported-versions window is a stated policy, and the compatibility suite covers its oldest
  member, because that client is still calling.
