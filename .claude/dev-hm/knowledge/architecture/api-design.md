# API design: contracts, versioning, compatibility, spec artifacts

An API contract is the part of a system other teams build against; it changes on a slower clock
than the code behind it, and its mistakes are the expensive kind — reversal means coordinating
every consumer. This file covers choosing the contract style, designing it, evolving it without
breaking consumers, and the OpenAPI/AsyncAPI artifacts that make the contract reviewable and
CI-checkable. Spec and standard versions are anchored in `knowledge/shared/versions.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Choosing the contract style | `knowledge/architecture/api-design/choosing-the-contract-style.md` |
| Contract design — REST | `knowledge/architecture/api-design/contract-design--rest.md` |
| Contract design — gRPC / protobuf | `knowledge/architecture/api-design/contract-design--grpc--protobuf.md` |
| Contract design — async messages | `knowledge/architecture/api-design/contract-design--async-messages.md` |
| Versioning and deprecation | `knowledge/architecture/api-design/versioning-and-deprecation.md` |
| Compatibility rules | `knowledge/architecture/api-design/compatibility-rules.md` |
| OpenAPI / AsyncAPI artifacts | `knowledge/architecture/api-design/openapi--asyncapi-artifacts.md` |
