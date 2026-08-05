# Test data management — Fixture drift

Section of `knowledge/quality/test-data-management.md`.


A fixture is a snapshot of yesterday's contract. Code moves; committed data does not; the suite
turns green-but-wrong. Drift shows up as:

- Fixtures missing fields added since, silently repaired by defaults — the tests never exercise
  the new field's handling, and coverage of it is illusory.
- Recorded API cassettes and sampled payloads replaying response shapes the upstream no longer
  sends, so integration tests certify compatibility with a retired contract.
- Enum values, formats, or units retired in production but alive in fixtures — tests pass,
  production parsing fails.
- Absolute dates and embedded certificates that age: the fixture that starts failing every
  January, or the suite that breaks when a test certificate expires.

Countermeasures, in order of leverage:

1. Generate, don't commit: fixtures produced by the same builders/serializers as production
   code drift only if the code does — which the compiler and contract tests catch.
2. Validate committed fixtures against the current schema in CI (JSON Schema/OpenAPI/protobuf
   validation of fixture files); a schema change that invalidates a fixture then fails loudly
   instead of silently defaulting.
3. Move with the contract: a diff that changes a wire contract or schema updates the fixtures
   that encode it in the same change — the fixture-flavored reading of QUA-017
   (`knowledge/quality/api-compatibility.md`).
4. Re-record cassettes on a schedule or on provider contract bumps, and review the re-recorded
   diff like code.
5. Prefer long-dated, generated-at-build test certificates and relative dates over committed
   expiring artifacts.
