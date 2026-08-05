# Accessibility

Accessibility is the part of interaction capability (ISO/IEC 25010:2023) we build into the UI
during development and verify like any other control — not an audit bolted on before release.
This file defines the controls our UI stacks ship by default — semantic structure, accessible
names, keyboard operability, contrast, focus management — and how each is verified, split into
what automation decides deterministically and what a short manual pass covers. Retrofitting any
of these costs a multiple of building them in; the cheapest accessible screen is the one that was
never inaccessible.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Conformance target | `knowledge/quality/accessibility/conformance-target.md` |
| Semantic structure | `knowledge/quality/accessibility/semantic-structure.md` |
| Labels | `knowledge/quality/accessibility/labels.md` |
| Keyboard operability | `knowledge/quality/accessibility/keyboard-operability.md` |
| Contrast | `knowledge/quality/accessibility/contrast.md` |
| Focus management | `knowledge/quality/accessibility/focus-management.md` |
| Automated checks | `knowledge/quality/accessibility/automated-checks.md` |
| Manual checks | `knowledge/quality/accessibility/manual-checks.md` |
