# Secrets and key management — Crypto-shredding versus backups

Section of `knowledge/security/secrets-and-keys.md`.


Crypto-shredding — destroying a key to render its ciphertext unreadable — is the deletion
mechanism `knowledge/security/data-classification.md#retention` names for stores where physical
purge is impractical (backups, immutable storage, replicas). It pulls in exactly the opposite
direction from the two sections above: every backup, replica, and escrow share of a key is a
path by which "deleted" data comes back. Both promises can be kept, but only by design:

- Deletion is only as complete as the key-copy inventory. Crypto-shredding is claimable for a
  dataset only when every copy of its key — replicas, escrow shares, drill artifacts — is
  enumerated and destroyed, and the deletion record lists each one (the KMS deletion events and
  the escrow custodians' destruction attestations are the evidence).
- Scope keys to the deletion unit. Per-tenant or per-purpose DEKs (`#key-separation`) make
  shredding surgical; one KEK over everything makes it all-or-nothing, which in practice means
  never.
- A key under DR escrow cannot anchor a crypto-shredding promise: if the business requires the
  key to survive every disaster, deletion of its data must use purge, or the data must be
  re-keyed to a shreddable DEK first. State which mechanism each Tier-3 store uses; the
  retention entry (SEC-144) reads that statement.
- Backups interact with shredding by outliving it: a restore from before the shred brings
  ciphertext back, which is harmless exactly as long as the key is gone everywhere. The drill
  above doubles as the proof — a post-shred drill asserts the sample does not decrypt.
