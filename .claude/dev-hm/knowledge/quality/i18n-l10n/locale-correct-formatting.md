# Internationalization and localization — Locale-correct formatting

Section of `knowledge/quality/i18n-l10n.md`.


Store canonical, format at the edge:

- Persist and transmit locale-independent representations: UTC instants plus an IANA time-zone
  name where local wall time matters, ISO 8601 in interchange, decimal types for money with an
  explicit currency code. Formatting into a locale happens at the presentation edge, parsing
  from a locale happens at the input edge, and nothing in between touches localized strings.
- Format with the platform's CLDR-backed APIs (`Intl` in JS/TS, `java.time` formatters and ICU
  on the JVM/Android, `CultureInfo`-aware formatting in .NET, `intl` in Flutter) — never by
  string concatenation or hand-rolled patterns. Locale data is a moving dataset, not a set of
  constants to inline.
- The invariant-culture rule (.NET's canonical bug class, present in every stack): machine
  interchange — serialization, log fields, cache keys, SQL literals — uses the invariant/root
  locale; user display uses the user's locale. A `double.ToString()` that follows the server's
  ambient locale writes `3,14` into a JSON payload on a German host.
- Parsing is symmetric: `1.234` is one-and-a-fraction in en, one-thousand-and-change in de.
  Numeric input from users is parsed with their locale; numeric input from machines is parsed
  invariant. A parser that accepts both silently accepts wrong numbers.
- Dates render per locale (order, separators, month names, calendar), and the edge-case classes
  in `knowledge/quality/test-strategy.md#edge-cases` (DST transitions,
  timezone-naive/aware mixing) apply with extra force once multiple zones are in play.
