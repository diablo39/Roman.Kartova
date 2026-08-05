# Quality oracle — Dependency hygiene

Section of `oracles/quality-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-060 | Addition justified | The handoff records why each added dependency is needed and which standard-library or existing-dependency alternative was rejected | S2 | ISO maintainability | knowledge/quality/review-method/dependency-gates.md |
| QUA-061 | Alive and licensed | Each added dependency shows a release or commit within the last 12 months and a license compatible with the project; both recorded | S2 | — | knowledge/quality/review-method/dependency-gates.md |
| QUA-062 | Deterministic versions | Zero wildcard or "latest" version ranges introduced; manifest and lockfile agree | S1 | — | knowledge/quality/review-method/dependency-gates.md |
| QUA-063 | Generated files not hand-edited | Zero manual edits to files marked or known as generated (lockfiles, generated clients, schema outputs); regeneration commands are used and recorded | S1 | ISO maintainability | knowledge/quality/review-method/dependency-gates.md |
