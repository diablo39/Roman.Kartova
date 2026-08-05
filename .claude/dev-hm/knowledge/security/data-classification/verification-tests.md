# Data classification — Verification tests

Section of `knowledge/security/data-classification.md`.


The control tests this file's controls map to, per
`knowledge/security/control-verification-tests.md`:

- Contract shape: serialized responses, events, and exports match the pinned allowlist; an
  unreviewed field fails.
- At-rest marker: synthetic Tier-3 plaintext absent from raw storage bytes; mechanism named in
  the handoff.
- Masking: masked shape asserted on render and export; full-value access refused without the
  declared permission.
- Telemetry canary: synthetic Tier-3 markers absent from every log, span, metric, analytics, and
  error sink.
- Erasure round trip: subject deleted or unreadable across primary and derived stores; TTL
  expiry removes under clock control.
- Residency: declared regions of resources match the project constraint.
