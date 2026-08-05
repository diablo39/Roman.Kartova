# Maintainability and technical-debt control

The per-diff maintainability limits live in
`knowledge/quality/quality-characteristics/maintainability.md-thresholds`. This file covers what
those limits cannot see: erosion across many diffs. Each individually justified exception, each
slightly-too-large function, each temporary workaround passes its own review — and still sums to
a codebase that resists change. The controls here make that accumulation visible, recorded, and
repaid on a schedule. Debt is not a failure; unrecorded debt is. The organizing frame is the five
maintainability sub-characteristics of ISO/IEC 25010:2023.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Sub-characteristics as controls | `knowledge/quality/maintainability-debt/sub-characteristics-as-controls.md` |
| Maintainability thresholds | `knowledge/quality/maintainability-debt/maintainability-thresholds.md` |
| Dependency cycles | `knowledge/quality/maintainability-debt/dependency-cycles.md` |
| Debt recording | `knowledge/quality/maintainability-debt/debt-recording.md` |
| Paydown discipline | `knowledge/quality/maintainability-debt/paydown-discipline.md` |
| Deprecation hygiene | `knowledge/quality/maintainability-debt/deprecation-hygiene.md` |
| Dependency currency | `knowledge/quality/maintainability-debt/dependency-currency.md` |
| Complexity trends | `knowledge/quality/maintainability-debt/complexity-trends.md` |
| Refactoring under behavior parity | `knowledge/quality/maintainability-debt/refactoring-under-behavior-parity.md` |
| Verification | `knowledge/quality/maintainability-debt/verification.md` |
