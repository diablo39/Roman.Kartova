# Data classification — Encryption

Section of `knowledge/security/data-classification.md`.


Tier-3 data at rest is encrypted by a named mechanism — named, because "the platform encrypts
everything" is a default, not a decision. The layer is chosen by who must not be able to read
the data; the layer map and its verification tests are in
`knowledge/security/cryptography-lifecycle.md#at-rest`, and the application-layer envelope shape
is in `knowledge/security/secrets-and-keys.md`. The tier-specific decisions:

- Payment data: the account number is unreadable wherever it is stored — strong encryption,
  truncation, tokenization, or keyed hashing. Prefer not storing it at all: the processor's
  token does the job in almost every flow. Sensitive authentication data (card verification
  codes, full track data) is never persisted after authorization — our schemas have no column
  for it, which is the strongest form of the control.
- Credentials: password KDFs per `knowledge/security/authentication-sessions.md`; API keys
  stored as keyed hashes.
- Personal and health identifiers: field-level or envelope encryption when the store is shared,
  when backups leave our control, or when erasure duties apply — per-subject or per-tenant keys
  make the retention section's crypto-shredding possible.
- Tier 2: platform storage-level encryption is the accepted baseline; escalate the layer only
  when the threat model says so.

Verification: the synthetic-marker test from the at-rest section — the marker written through
our code is absent from raw storage bytes — plus the mechanism named in the handoff so the gate
can check the decision, not just the effect.
