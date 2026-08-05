# Secrets and key management — Secrets out of logs and telemetry

Section of `knowledge/security/secrets-and-keys.md`.


Supports SEC-021, SEC-022. The review cues are in
`knowledge/security/secure-coding-review.md#secrets`; the durable in-code controls are:

- Secret-typed wrappers whose string representation redacts — `SecretStr`, a Rust newtype with a
  redacting `Debug` impl, a C# type overriding `ToString`. Accidental logging then prints a mask,
  not the value. This is the primary control; it works at every sink at once.
- A scrubber at the logging sink (field-name denylist plus known credential patterns) as a
  backstop — it catches what typing missed, and its miss rate is why it is not the primary.
- Error and crash reporters configured to strip request bodies, headers, and cookies before send.

The control test drives a request carrying a known synthetic token through the system and asserts
the marker appears in no log line, span, or error payload — the same canary pattern as
`knowledge/security/data-classification.md#telemetry`, with secrets as the payload. Pattern
details: `knowledge/security/control-test-patterns-dataflow.md`.
