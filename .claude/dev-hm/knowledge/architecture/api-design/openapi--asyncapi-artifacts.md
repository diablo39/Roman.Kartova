# API design: contracts, versioning, compatibility, spec artifacts — OpenAPI / AsyncAPI artifacts

Section of `knowledge/architecture/api-design.md`.


The spec file is the contract's source of truth (current spec versions per
`knowledge/shared/versions.md`): OpenAPI for request/response APIs, AsyncAPI for channels,
messages, and operations of the async surface. Working rules:

- One spec per API, in the producer's repo, reviewed like code — contract diffs are the
  highest-leverage review surface in the repo. Design-first (spec before implementation) for
  cross-team APIs so review happens while change is cheap; code-first generation is acceptable
  for internal APIs when the generated spec is committed, diffed, and gated the same way.
  TypeSpec-style authoring languages that compile to OpenAPI slot into the same pipeline.
- Make the spec complete enough to be the documentation: descriptions, examples per operation,
  error shapes (Problem Details) with their `type` values enumerated.
- CI pipeline per contract change: lint (Spectral or equivalent, with the org ruleset —
  naming, pagination, error-shape rules encoded, not memoed), breaking-change diff (oasdiff /
  `buf breaking` — fail on breaking, warn on additive), validate examples against schemas,
  publish rendered reference docs (Redoc/Scalar-class renderer) and, where used, generated
  clients. These gates are contract fitness functions in the sense of
  `architecture-evaluation.md`.
- Record contract-shaping decisions (style choice, versioning scheme, deprecation policy) as
  ADRs (`adr-practice.md`); the API guidelines the linter enforces are the ADRs' executable
  form.

Sources: RFC 9110/9111 (HTTP semantics/caching), RFC 9457 (Problem Details), RFC 9745
(Deprecation) + RFC 8594 (Sunset), spec.openapis.org, asyncapi.com, buf.build/docs,
microservices.io; version anchors in `knowledge/shared/versions.md`.
