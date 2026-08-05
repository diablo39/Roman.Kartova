# Resource protection — Regex safety

Section of `knowledge/security/resource-protection.md`.


A regular expression evaluated against external input is work the caller shapes (CWE-1333).
Our code prefers the stack's linear-time engine where one exists (RE2-style libraries, Rust's
regex crate, the non-backtracking option in .NET); where only a backtracking engine is
available, every match against external input runs under a match timeout. Patterns are code:
they are never built by interpolating external input, and validation patterns favor anchored,
bounded quantifiers over nested unbounded repetition. The same reasoning applies to other
caller-shaped computation — glob matching, XML XPath, custom parsers.

Verification tests: each validation pattern is exercised against generated worst-case-length
inputs and completes within a per-match budget (property-based tests do this well); where a
match timeout is the control, a test observes it trip and the operation fail closed with the
declared error.
