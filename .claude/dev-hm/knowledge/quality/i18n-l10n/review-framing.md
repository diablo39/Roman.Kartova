# Internationalization and localization — Review framing

Section of `knowledge/quality/i18n-l10n.md`.


Typical findings, tagged for the report
(`knowledge/quality/review-method.md`):

- `finding S1 — functional suitability (correctness): amount parsed with ambient locale;
  de host reads 1.234 as 1234` (money/data paths rate S1)
- `finding S2 — functional suitability: date formatted by string concatenation, ignores locale`
- `finding S2 — interaction capability: user-visible literal not externalized; untranslatable`
- `finding S2 — interaction capability: physical left/right layout; breaks under RTL`
- `finding S3 — maintainability: locale-dependent test golden; suite fails on non-en runner`

Severity follows blast radius as usual: wrong-value defects (parsing, money, units) sit above
wrong-rendering defects; both sit above missing-translation cosmetics.
