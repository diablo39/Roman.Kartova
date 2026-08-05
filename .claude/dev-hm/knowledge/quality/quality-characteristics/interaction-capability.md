# Quality characteristics in review (ISO/IEC 25010:2023) — Interaction capability

Section of `knowledge/quality/quality-characteristics.md`.


Sub-characteristics include operability, learnability, user error protection, user engagement,
inclusivity, self-descriptiveness. A deterministic subset is enforced: configured accessibility
checks pass on changed UI files (QUA-110) and new interactive elements carry accessible names
(QUA-111) — method, conformance target, and per-stack tooling in
`knowledge/quality/accessibility.md`. The rest is not decidable from a diff; for UI-bearing
stacks the review still asks: are error messages actionable, are destructive actions
confirmable/undoable, does the change respect the platform's accessibility conventions (labels,
contrast, focus order)? Record such observations as findings; Flutter and web specifics live in
the stack knowledge files. Locale and language correctness — encoding, formatting, translation
completeness, RTL layout — is reviewed as functional-suitability and interaction-capability
findings per `knowledge/quality/i18n-l10n.md`.
