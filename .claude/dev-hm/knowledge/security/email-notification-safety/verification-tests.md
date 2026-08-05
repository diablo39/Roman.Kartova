# Email and notification safety — Verification tests

Section of `knowledge/security/email-notification-safety.md`.


Control tests with the fails-when-removed lever named:

- Header injection: a display name and a subject containing CR/LF probes produce either a
  refusal or a message whose parsed form still has exactly the intended recipients and headers
  — parse the captured message, count headers. Lever: bypassing the typed API in a scratch run
  makes the recipient-count assertion fail.
- Encoding: a markup probe in a user-supplied field renders encoded in the captured HTML body
  and intact in the plain-text part — no element created from data. Lever: switching the
  template binding to raw makes the no-element assertion fail.
- Link base: with forged `Host`/`X-Forwarded-Host` on the triggering request, every link in the
  captured message starts with the configured base (the canary test above). Lever: deriving the
  base from the request makes it fail.
- Token hygiene: reset and verification links from the outbox work exactly once and not after
  expiry — driven through the real redemption endpoint with an injected clock.
- Flooding bound: sends past the per-recipient cap are refused and the outbox count stays at
  the cap.
- Inbound callbacks: unsigned, tampered, stale, and duplicate provider callbacks are refused or
  deduplicated per the tests in `knowledge/security/webhook-integrations.md#verification-tests`;
  a forged bounce for a message identifier we never sent changes no delivery state.
