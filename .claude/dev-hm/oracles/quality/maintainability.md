# Quality oracle — Maintainability

Section of `oracles/quality-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-020 | Boundary compliance | Zero new dependencies that violate a documented architecture rule (layer or module boundary); n/a when the repository documents no such rule | S1 | ISO maintainability (modularity) | knowledge/quality/quality-characteristics/maintainability.md |
| QUA-021 | No copy-paste duplication | The diff introduces no block of ten or more lines duplicated from elsewhere in the repository; shared logic is extracted instead | S2 | ISO maintainability (reusability) | knowledge/quality/quality-characteristics/maintainability.md-thresholds |
| QUA-022 | Analyzable units | New functions are at most 60 lines with cyclomatic complexity at most 10, or carry an inline comment justifying the exception | S2 | ISO maintainability (analysability) | knowledge/quality/quality-characteristics/maintainability.md-thresholds |
| QUA-023 | No dead code | Zero commented-out code blocks committed; zero unused new imports, variables, or functions (unused-symbol analysis clean) | S3 | ISO maintainability | knowledge/quality/quality-characteristics/maintainability.md-thresholds |
| QUA-024 | Named constants | Numeric literals other than -1, 0, 1, 2 that appear more than once in new code are extracted to named constants | S3 | ISO maintainability (modifiability) | knowledge/quality/quality-characteristics/maintainability.md-thresholds |
| QUA-025 | Lint and format errors clean | The repository's configured linter and formatter report zero errors on changed files; command recorded | S1 | ISO maintainability | knowledge/quality/review-method/lint-and-type-gates.md |
| QUA-026 | No dependency cycles | The diff introduces no import or package cycle between modules; verified with the repository's import-graph tool where one exists, otherwise by tracing the imports the diff adds | S2 | ISO maintainability (modularity) | knowledge/quality/quality-characteristics/maintainability.md-thresholds |
| QUA-027 | No new lint warnings | The configured linter reports zero new warnings on changed files; command recorded | S2 | ISO maintainability | knowledge/quality/review-method/lint-and-type-gates.md |
