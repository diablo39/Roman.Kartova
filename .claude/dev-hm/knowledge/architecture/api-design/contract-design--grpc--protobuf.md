# API design: contracts, versioning, compatibility, spec artifacts — Contract design — gRPC / protobuf

Section of `knowledge/architecture/api-design.md`.


- Version the package (`billing.v1`), not individual messages; a breaking change means a new
  package version served alongside the old.
- Field numbers are the wire contract: never reuse or renumber; `reserved` removed fields and
  names. All fields are optional on the wire — design so that absent fields have safe defaults.
- Enums get a zero `_UNSPECIFIED` value; consumers must handle unknown enum values (they will
  arrive mid-rollout).
- Errors: `google.rpc.Status` with typed detail messages, mirroring the Problem Details role.
- Express validation as schema annotations (protovalidate) so constraints live in the contract,
  not in per-language duplication.
- Lint and breaking-change-check the schema in CI with buf (`buf lint`, `buf breaking` against
  the main branch) — the protobuf analogue of the OpenAPI pipeline below.
