# Secrets and key management — Rotation

Section of `knowledge/security/secrets-and-keys.md`.


Rotation is designed in from the first key, because the compromise response depends on it being
routine rather than heroic:

- Dual-accept window: verification and decryption accept the active keyset (current plus
  retired-but-valid versions); signing and encryption use only the primary. Rollout order: add
  new key to the accept set everywhere, then promote it to primary, then retire the old.
- Re-encryption strategy chosen per store: lazy (re-encrypt on next write) with a completion
  metric, or a batched backfill job — see `knowledge/quality/data-migration-safety.md` for the
  backfill discipline.
- Automated cadence for high-value keys, and rotation exercised in staging on a schedule, so the
  first real rotation is not performed for the first time during an incident.

Verification tests: data written under key v1 still reads after v2 becomes primary; new writes
carry v2's ID; a token signed by a destroyed key version is refused. Removing the dual-accept
logic makes these tests fail — which is the point (see
`knowledge/security/control-verification-tests.md`).
