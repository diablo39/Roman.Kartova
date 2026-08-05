# Secrets and key management — Key backup and escrow

Section of `knowledge/security/secrets-and-keys.md`.


Losing a decryption key loses the data it protects — permanently, which is the availability
half of key management. Whether a key gets a backup path is decided per key at creation, in the
same record as its owner and cryptoperiod, from one question: what does losing it cost?

- Keys living in a managed KMS: durability of the material is the provider's job; our part of
  the backup story is policy and reachability (next section), not copies.
- Signing and transport keys: prefer re-issue over backup. A lost TLS or token-signing key is
  replaced by rotation, and every copy that exists to "save" it is a standing compromise
  surface protecting against a cheap event.
- Decryption keys whose loss strands data (an imported KEK, an offline root, HSM-resident
  material with no provider replication): these get deliberate escrow — an encrypted export
  held under split custody with a quorum to reconstruct (M-of-N key shares), stored at the
  same protection level as the original and covered by the same access audit. Escrow to a
  weaker place (a bucket, a password manager, a runbook attachment) is a second attack surface,
  not a backup.

Every escrow copy is inventoried on the key's record. The inventory is load-bearing twice: the
compromise response must rotate every copy, and crypto-shredding (below) is only true if the
inventory is complete.
