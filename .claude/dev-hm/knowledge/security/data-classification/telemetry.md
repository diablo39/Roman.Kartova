# Data classification — Telemetry

Section of `knowledge/security/data-classification.md`.


Tier-3 values stay out of logs, traces, metric labels, analytics events, error payloads, and
crash reports. This extends the log-focused review rule (SEC-082 in
`knowledge/security/secure-coding-review.md`) across every telemetry sink our code writes to:

- Structured logging with enumerated fields: log call sites pass named scalar fields, never
  whole request or entity objects; the typed wrappers from the tiers section redact themselves
  on serialization.
- Nothing regulated in URLs: query strings and path segments land in access logs, proxy logs,
  and referrer headers that no application-level redaction touches. Identifiers travel in
  bodies, or as opaque tokens when they must appear in a URL.
- Traces, metrics, analytics: span attributes and metric labels come from an enumerated
  allowlist; subjects appear as opaque IDs, never as emails or names; analytics events carry a
  defined property schema rather than free-form payloads.
- Error and crash reporters strip request bodies, headers, cookies, and local variables before
  send — configured centrally, asserted by the canary below.
- A sink-side scrubber (field-name denylist plus known data-shape patterns) runs as backstop;
  its job is catching what the typed wrappers missed, and its miss rate is why it is the second
  line, not the first.
- Caches count as sinks: responses carrying authenticated or Tier-3 content set
  `Cache-Control: no-store` so shared proxies and browser caches keep no copy
  (`knowledge/security/browser-protections.md#response-cache-hygiene`).

Verification: the canary test — drive a request carrying synthetic Tier-3 markers (a fake email,
a card-shaped number that passes checksum, a synthetic health code) end to end, then assert the
markers appear in no log line, span, metric, analytics payload, or error report. Same shape as
the secrets canary in `knowledge/security/secrets-and-keys.md`; pattern details in
`knowledge/security/control-test-patterns-dataflow.md`.
