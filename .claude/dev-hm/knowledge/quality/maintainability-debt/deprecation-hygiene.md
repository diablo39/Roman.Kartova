# Maintainability and technical-debt control — Deprecation hygiene

Section of `knowledge/quality/maintainability-debt.md`.


Deprecating is a three-part promise, and all three parts are checkable:

- Marked in the stack's own mechanism — `@Deprecated`, `[Obsolete]`, `#[deprecated]`,
  `@deprecated` doc tags — so the compiler or linter warns at every call site.
- The message names the replacement. "Deprecated" without "use X instead" strands every caller.
- A removal condition is stated (a version or a date), and removal happens. A deprecation older
  than its removal condition is a debt entry.

The trend control: the count of deprecation warnings in CI does not rise. New code calling a
deprecated API is a review finding — the warning was the message, and the message was ignored.
