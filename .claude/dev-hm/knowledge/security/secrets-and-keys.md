# Secrets and key management

The controls that keep secret material managed, not merely unleaked. The diff-review quick
reference for leakage lives in `knowledge/security/secure-coding-review/secrets.md` (SEC-020 –
SEC-023); this file covers the discipline behind those entries — how secrets reach our code, how
keys live and die, and the tests that prove each control holds. Cryptographic algorithm choice is
in `knowledge/security/cryptography-lifecycle.md`; classification of the data keys protect is in
`knowledge/security/data-classification.md`. Handling rules for agents themselves are in
`knowledge/shared/ground-rules.md`.

A secret is anything whose disclosure grants capability: credentials, API keys, tokens, private
keys, connection strings with passwords, webhook signing secrets. A key is a secret with a
cryptographic job; keys get every secret control plus a lifecycle of their own.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| One access path | `knowledge/security/secrets-and-keys/one-access-path.md` |
| Workload identity | `knowledge/security/secrets-and-keys/workload-identity.md` |
| Injection into the process | `knowledge/security/secrets-and-keys/injection-into-the-process.md` |
| Secrets out of logs and telemetry | `knowledge/security/secrets-and-keys/secrets-out-of-logs-and-telemetry.md` |
| Key lifecycle | `knowledge/security/secrets-and-keys/key-lifecycle.md` |
| Rotation | `knowledge/security/secrets-and-keys/rotation.md` |
| Key separation | `knowledge/security/secrets-and-keys/key-separation.md` |
| Envelope encryption | `knowledge/security/secrets-and-keys/envelope-encryption.md` |
| Key backup and escrow | `knowledge/security/secrets-and-keys/key-backup-and-escrow.md` |
| KMS disaster recovery | `knowledge/security/secrets-and-keys/kms-disaster-recovery.md` |
| Restore drills | `knowledge/security/secrets-and-keys/restore-drills.md` |
| Crypto-shredding versus backups | `knowledge/security/secrets-and-keys/crypto-shredding-versus-backups.md` |
| Compromise response | `knowledge/security/secrets-and-keys/compromise-response.md` |
| Verification tests | `knowledge/security/secrets-and-keys/verification-tests.md` |
