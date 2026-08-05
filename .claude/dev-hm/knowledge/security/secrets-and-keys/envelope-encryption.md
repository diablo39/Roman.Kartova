# Secrets and key management — Envelope encryption

Section of `knowledge/security/secrets-and-keys.md`.


The standard shape for application-layer encryption at rest: a data-encryption key (DEK) encrypts
the data locally with an AEAD cipher; a key-encryption key (KEK) held in the KMS wraps the DEK;
the wrapped DEK and the KEK's ID are stored alongside the ciphertext.

Properties this buys, each verifiable: the KEK never leaves the KMS/HSM (there is no code path
that could log or leak it); bulk data never transits the KMS (only DEK wrap/unwrap calls); KEK
rotation re-wraps DEKs without touching the data; every key use is an auditable IAM event. Where
this sits among the at-rest options — and when storage-level encryption is enough — is covered in
`knowledge/security/cryptography-lifecycle.md#at-rest`.
