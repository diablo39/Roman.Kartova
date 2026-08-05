# Internationalization and localization — Pseudo-localization

Section of `knowledge/quality/i18n-l10n.md`.


The cheapest i18n test there is: a generated fake locale that transforms every externalized
message — accented characters (Ⓣⓔⓢⓣ-style mappings), ~40% length expansion, bracket markers
around each message — and is run through the app like a real language.

What it catches deterministically: hardcoded strings (they appear unaccented, without
brackets), truncation and clipped layouts (the expansion), concatenation (brackets split
mid-sentence), encoding faults (the non-ASCII characters), and placeholder corruption. Platform
support is built in on the major stacks (Android's `en-XA`/`ar-XB` pseudo-locales, an Xcode
scheme option on iOS, library support on web and Flutter) — details live in the stack knowledge
files. Run the UI test suite once under the pseudo-locale and once under the RTL pseudo-locale;
that pass is the i18n equivalent of the accessibility automated check
(`knowledge/quality/accessibility.md#automated-checks`).
