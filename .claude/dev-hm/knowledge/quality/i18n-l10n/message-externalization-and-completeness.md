# Internationalization and localization — Message externalization and completeness

Section of `knowledge/quality/i18n-l10n.md`.


- Every user-visible string lives in the localization resource system, keyed, with named
  placeholders — zero user-visible literals in code. Concatenating translated fragments
  ("Deleted " + n + " items") is untranslatable: word order, agreement, and pluralization all
  differ by language.
- Plurals and gender go through the message-format system's plural/select machinery. CLDR
  plural categories differ per language (English has two; Polish and Arabic more), so an
  `if (n == 1)` in code is wrong in most target languages. ICU MessageFormat is the established
  mechanism; its successor MessageFormat 2.0 is specified and stabilizing across
  implementations — adoption state per `knowledge/shared/versions.md`.
- Completeness is a CI check, not a hope: extract keys from code, diff against each locale's
  resources, and fail (or report) on missing and on orphaned keys. Declare the fallback chain
  explicitly (requested locale → base language → source language) and make fallback observable
  in logs or dev builds — silent fallback is how half-translated releases ship.
- Translation files are code: reviewed in diffs, placeholders validated against the source
  message (a translation that drops or renames a placeholder is a runtime error in one locale
  only), no machine-format mangling from translation round trips.
- Give translators context: source-adjacent comments or screenshots per key. "Book" the noun
  and "book" the verb translate differently, and the key name alone does not disambiguate.
