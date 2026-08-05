# Internationalization and localization — Locale test matrix

Section of `knowledge/quality/i18n-l10n.md`.


Exhaustive locale testing is impossible (ISTQB P2); pick pathological locales that each falsify
a different assumption:

| Locale | What it falsifies |
|---|---|
| de-DE | Decimal comma, dot thousands separator, 24h time, day-first dates, long compound words (layout) |
| ar (or ar-XB pseudo) | RTL layout, bidi embedding, plural categories beyond one/other |
| ja-JP | No plural distinction, no word spaces (wrapping), non-Latin measurement of "length" |
| tr-TR | Dotted/dotless-i casing — the machine-string case-operation bug |
| pl-PL | Multiple plural categories (one/few/many) — flushes `n == 1` logic |
| en-XA pseudo | Hardcoded strings, truncation, concatenation, encoding |
| A DST-observing and a non-observing zone pair | Date arithmetic and wall-time assumptions |

A per-diff review does not run the matrix; the repository's UI suite does, on the screens the
diff touches. The reviewer's question is narrower: does the diff add a user-visible string
outside the resource system, a hand-formatted value, a physical-direction layout, or a
locale-sensitive operation on a machine string?
