# Data classification — Retention

Section of `knowledge/security/data-classification.md`.


Data we no longer hold cannot leak, and regulated data usually must go — retention is a control
with a mechanism, not a policy document:

- Every Tier-3 store states a retention period and names the mechanism that enforces it: a TTL
  or expiry index, a scheduled purge job, or a cascade from the owning record. A stated period
  without a mechanism is a wish.
- Deletion reaches the copies: replicas follow the primary; caches and search indexes consume
  deletion events; backups either expire within a stated window or the restore runbook includes
  re-applying deletions recorded since the backup.
- Subject erasure has one entry point: a single code path deletes or anonymizes a subject across
  the stores that hold them. Where physical deletion is impractical — immutable backups,
  append-only stores — crypto-shredding stands in: destroy the per-subject or per-tenant key so
  the remaining ciphertext is unreadable (key design in
  `knowledge/security/secrets-and-keys.md#key-separation`, derivation in
  `knowledge/security/cryptography-lifecycle.md#key-derivation`).
- Purge jobs are idempotent, batched, and observable — a metric for records purged and for the
  age of the oldest record still held is the retention control's own health signal; an alert on
  its growth is the version of this control's test that runs in production.

Verification: create a synthetic subject, exercise the erasure path, assert the subject's data
is gone or unreadable in the primary store and the search index; a TTL test under clock control
asserts expiry actually removes rather than merely hides.
