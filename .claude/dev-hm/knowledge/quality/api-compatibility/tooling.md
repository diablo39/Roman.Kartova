# API compatibility and versioning — Tooling

Section of `knowledge/quality/api-compatibility.md`.


Established mechanical checks, by contract type (specifics and versions live in the stack
knowledge files and `knowledge/shared/versions.md`):

| Contract type | Check |
|---|---|
| Protobuf/gRPC | Breaking-change linter over the IDL (buf-style `breaking` checks) against the last released state |
| OpenAPI/REST | Spec diff tools (oasdiff-class) classifying changes as breaking/non-breaking |
| Avro/JSON Schema events | Schema-registry compatibility mode enforced at registration |
| Consumer-driven | Contract broker verification against deployed consumer versions |
| Library API surface | Semver/API-diff checkers (cargo-semver-checks, apidiff/Revapi-class per stack) |

QUA-113 is the enforcement hook: when the repository configures any of these for a contract,
a diff changing that contract runs the check against the last published version and records
the result. The tools decide the structural taxonomy deterministically; the semantic rows
(meaning, units, ordering) remain the reviewer's, via the contract tests' value assertions.
