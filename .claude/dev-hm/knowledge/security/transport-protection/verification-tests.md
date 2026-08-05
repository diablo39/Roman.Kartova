# Transport protection — Verification tests

Section of `knowledge/security/transport-protection.md`.


The control tests this file's controls map to, per the mandate in
`knowledge/security/control-verification-tests.md`, with runnable patterns in
`knowledge/security/control-test-patterns-access.md`:

- Unverifiable peer refused: production client against a fixture endpoint outside its trust
  store — verification error asserted, zero requests received.
- No downgrade: plaintext recorder next to the failing TLS fixture stays silent; resilience
  wrappers re-verified with the same recorder.
- Floor holds: handshake against a below-floor-only fixture endpoint is refused.
- Peer identity required: internal endpoint refuses a request without a verified peer identity
  before handler logic runs.
- Configuration fixtures: no verification-disable flags, no below-floor protocol enablement, no
  classical-only named-group pins introduced by the diff.
