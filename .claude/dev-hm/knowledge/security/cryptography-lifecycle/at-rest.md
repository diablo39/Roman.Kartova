# Cryptography lifecycle — At rest

Section of `knowledge/security/cryptography-lifecycle.md`.


Encryption at rest is a layer choice, and the layer is chosen by who must not be able to read
the data — not by what is cheapest to declare:

| Layer | Protects against | Does not protect against |
|---|---|---|
| Storage/disk-level (platform default) | lost media, decommissioned disks, raw-volume access | any principal with database or application access |
| Database-native (TDE) | direct file and backup-file access | queries through a compromised application or DBA session |
| Column/field-level | database operators, shared-database neighbors, exported backups | compromise of the application holding the key |
| Application-layer envelope | store compromise entirely; enables per-tenant and per-subject shredding | compromise inside the application process |

The tier of the data picks the minimum (`knowledge/security/data-classification.md#encryption`):
Tier 3 requires a named mechanism decision, and the highest-sensitivity fields (credentials,
payment data) take field-level or envelope encryption — the envelope shape and its KMS properties
are in `knowledge/security/secrets-and-keys.md`. Backups, replicas, and search indexes inherit
the requirement of the data they copy.

Verification: for field- and application-layer encryption, a test writes a known synthetic marker
and asserts the raw storage bytes (or a dump of them) contain no plaintext marker; for
storage-level encryption, an infrastructure-as-code assertion that the encryption setting is on,
supplemented by behavior checks where the platform exposes them.
