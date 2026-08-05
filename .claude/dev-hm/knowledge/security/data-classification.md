# Data classification

The tiered handling scheme our code enforces for the data it stores and moves. Classification is
the routing decision: the tier a datum carries decides which protective controls apply to it —
minimization, encryption, masking, telemetry hygiene, retention, residency — and each control
comes with the test that proves it holds, per
`knowledge/security/control-verification-tests.md`. Encryption mechanics live in
`knowledge/security/cryptography-lifecycle.md`; credential handling in
`knowledge/security/secrets-and-keys.md`; the telemetry test patterns in
`knowledge/security/control-test-patterns-dataflow.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Tiers | `knowledge/security/data-classification/tiers.md` |
| Minimization | `knowledge/security/data-classification/minimization.md` |
| Encryption | `knowledge/security/data-classification/encryption.md` |
| Masking | `knowledge/security/data-classification/masking.md` |
| Telemetry | `knowledge/security/data-classification/telemetry.md` |
| Retention | `knowledge/security/data-classification/retention.md` |
| Residency | `knowledge/security/data-classification/residency.md` |
| Verification tests | `knowledge/security/data-classification/verification-tests.md` |
