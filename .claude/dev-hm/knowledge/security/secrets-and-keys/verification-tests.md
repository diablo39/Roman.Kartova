# Secrets and key management — Verification tests

Section of `knowledge/security/secrets-and-keys.md`.


The control tests this file's controls map to, per the mandate in
`knowledge/security/control-verification-tests.md`:

- Boot refusal: starting the service without a required secret fails fast with a clear error —
  asserted, not assumed.
- Redaction: `str`, `repr`, and JSON serialization of the settings object contain no secret bytes.
- Telemetry canary: a synthetic credential driven through a request appears in no log, span,
  metric, or error payload.
- Rotation round-trip: decrypt-old/sign-new behavior across a key promotion; retired-key refusal.
- Environment separation: staging-signed token refused by production verification; production key
  IDs absent from non-production fixtures.
- Replica policy parity: a configuration test asserts each key replica's access policy matches
  the primary's, so a permissive replica is a failing diff, not a quiet bypass.
- Restore drill record: the scheduled drill's output — key path exercised, sample decrypted,
  key versions used — exists and is current per the drill cadence.
- Shred completeness: after a crypto-shred in a test environment, a restore of the pre-shred
  backup cannot decrypt the sample, and the deletion record lists every destroyed key copy.
