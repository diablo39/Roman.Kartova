# Secrets and key management — KMS disaster recovery

Section of `knowledge/security/secrets-and-keys.md`.


Two failure classes, two controls:

- Region loss: where the platform offers multi-region key replication, keys protecting data
  that must survive a regional outage are created replicated, and ciphertext stored in a
  recovery region decrypts there without a call home. Each replica carries its own access
  policy — a parity check across replicas is part of the configuration tests, because a
  permissive replica policy is a quiet bypass of the primary's.
- Deletion, accidental or hostile: key deletion runs through the KMS's scheduled-deletion
  window at the maximum the platform allows, with an alert on schedule-deletion events wired to
  a named responder (`knowledge/security/security-logging-detection.md#severity-and-alerting`).
  Deleting a key is an irreversible, gated action — the deployment counterpart of an S0.

Order matters in the recovery runbook: data restore depends on key access, so the key path is
step one, verified before the data restore begins, and the drill below rehearses them together.
