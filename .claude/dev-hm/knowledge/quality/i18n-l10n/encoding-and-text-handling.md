# Internationalization and localization — Encoding and text handling

Section of `knowledge/quality/i18n-l10n.md`.


- UTF-8 end to end: source files, HTTP bodies, database columns and their collations, file
  exports. Encoding declared explicitly at every boundary that has a knob (database connection,
  file write, HTTP header) — an unstated default is a latent mojibake report from the first
  customer whose name has a diacritic.
- Normalize at comparison boundaries: the same visible string has multiple Unicode encodings
  (composed vs decomposed). Comparisons, deduplication, and lookups normalize (NFC as the
  common default) or they intermittently miss.
- "Length" is three different questions: bytes (storage limits), code points (API limits),
  grapheme clusters (what a user counts). Truncating by bytes or code points can cut a
  character in half — validate and truncate on grapheme boundaries for user-visible text.
- Case operations on machine strings are locale-independent by requirement: uppercasing a
  protocol token or map key with the user's locale breaks in Turkish (`i` ↔ `İ`, the canonical
  bug). Use the invariant/root-locale operation for machine strings, the user's locale only for
  display text.
- Sorting for display is locale-aware collation (CLDR-backed collators); sorting for machine
  stability (canonical orderings, test golden files) is binary/ordinal. Mixing the two directions
  is a drift generator.
