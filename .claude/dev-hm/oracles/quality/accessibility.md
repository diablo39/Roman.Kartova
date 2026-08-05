# Quality oracle — Accessibility

Section of `oracles/quality-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-110 | Accessibility checks clean | When the repository configures accessibility checks (web a11y lint, axe CI, Flutter accessibility lints), changed UI files pass with zero new errors; n/a when not configured | S2 | ISO interaction capability (inclusivity) | knowledge/quality/accessibility/automated-checks.md |
| QUA-111 | Interactive elements labeled | New interactive UI elements carry an accessible name per the stack's convention, and new images carry either a label/alt text or an explicit decorative marker (`alt=""`, `excludeFromSemantics`); misses itemized per element; whether a name or text conveys the right meaning goes to the manual pass as `finding` lines (knowledge/quality/accessibility/manual-checks.md) | S2 | ISO interaction capability (inclusivity, self-descriptiveness) | knowledge/quality/accessibility/labels.md |
