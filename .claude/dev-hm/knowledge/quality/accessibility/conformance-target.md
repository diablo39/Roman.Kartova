# Accessibility — Conformance target

Section of `knowledge/quality/accessibility.md`.


The target is the current WCAG version at Level AA (version anchor in
`knowledge/shared/versions.md`). WCAG is written for the web; its four principles — perceivable,
operable, understandable, robust — apply to Flutter and other native UIs through each platform's
accessibility API, and the controls below are stated for both. European product law (the
European Accessibility Act, applied through the harmonized EN 301 549 standard) makes Level AA
the legal floor for software sold in the EU, so the target is a shipping requirement.

Scope of the claim: "meets the target" in a handoff means the automated checks below ran clean
and the manual pass below was done — never automated checks alone, which cover only part of the
criteria. Individual Level AAA criteria are adopted where they are cheap (enhanced contrast on
data-dense screens is a common example) but AAA is not claimed.
