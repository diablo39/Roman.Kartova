# Quality oracle — Documentation

Section of `oracles/quality-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-050 | Public API documented | New public functions, classes, and endpoints carry doc comments stating purpose, parameters, and error behavior | S2 | ISO maintainability | knowledge/quality/review-method/documentation-review.md |
| QUA-051 | Config documented | New config keys, environment variables, and feature flags are documented where the repository documents them (env example file, README, config reference) | S2 | ISO maintainability, flexibility (installability) | knowledge/quality/review-method/documentation-review.md |
| QUA-052 | Structural decisions recorded | Changes with structural impact (new service, new dependency direction, storage change) reference an ADR; n/a for non-structural diffs | S2 | ISO maintainability | knowledge/architecture/adr-practice.md |
| QUA-053 | User-facing changes documented | Changes that alter user-visible behavior, CLI flags, or API contracts update the repository's changelog or user docs where it maintains them; n/a when it maintains none | S3 | ISO maintainability | knowledge/quality/review-method/documentation-review.md |
