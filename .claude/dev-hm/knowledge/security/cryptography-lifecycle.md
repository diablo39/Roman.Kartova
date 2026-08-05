# Cryptography lifecycle

The controls that keep data protected in transit, at rest, and in use, and the discipline that
keeps our cryptographic choices strong over time — algorithm selection, weak-primitive refusal,
crypto agility, and the post-quantum transition. Key management (generation, rotation, envelope
encryption, separation) is in `knowledge/security/secrets-and-keys.md`; transport policy depth is
in `knowledge/security/transport-protection.md`; which data must be encrypted at which tier is
decided by `knowledge/security/data-classification.md`. Every control here follows the mandate in
`knowledge/security/control-verification-tests.md`: our code keeps a strong setting; a test
asserts the setting holds and cannot silently weaken.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Algorithm selection | `knowledge/security/cryptography-lifecycle/algorithm-selection.md` |
| Weak primitives | `knowledge/security/cryptography-lifecycle/weak-primitives.md` |
| In transit | `knowledge/security/cryptography-lifecycle/in-transit.md` |
| At rest | `knowledge/security/cryptography-lifecycle/at-rest.md` |
| In use | `knowledge/security/cryptography-lifecycle/in-use.md` |
| Key derivation | `knowledge/security/cryptography-lifecycle/key-derivation.md` |
| Crypto agility | `knowledge/security/cryptography-lifecycle/crypto-agility.md` |
| No silent downgrade | `knowledge/security/cryptography-lifecycle/no-silent-downgrade.md` |
| Post-quantum | `knowledge/security/cryptography-lifecycle/post-quantum.md` |
| Verification tests | `knowledge/security/cryptography-lifecycle/verification-tests.md` |
