# Webhook integrations — Verification tests

Section of `knowledge/security/webhook-integrations.md`.


Control tests per `knowledge/security/control-verification-tests.md`, each with its
fails-when-removed lever:

- Unsigned and mis-signed: a delivery with no signature, and one whose body was altered after
  signing, are refused with the refusal status, zero handler invocations, zero side effects.
  Lever: detaching the verification middleware in a scratch run makes both pass through and the
  tests fail.
- Wrong key: a delivery signed with a key outside the configured keyset is refused; during a
  declared rotation overlap, deliveries signed with old and new keys both verify — both
  directions asserted, so rotation neither breaks nor never-expires.
- Stale and future: deliveries timestamped just outside the tolerance window (both directions,
  injected clock — never sleeps) are refused; just-inside is accepted. Lever: widening the
  window in a scratch run flips the just-outside probes.
- Replay: the same verified delivery presented twice yields one side effect — assert the
  processed count and the second response's acknowledged-duplicate shape. Lever: dropping the
  unique constraint or dedupe write makes the count assertion fail.
- Concurrent duplicate: two copies of one delivery processed concurrently produce one side
  effect — the constraint, not the check, is what the test proves.
- Outbound destination: registering or sending to a URL that is non-`https`, resolves private,
  or answers with a redirect is refused, and the recorder endpoint behind it receives zero
  requests (`knowledge/security/control-test-patterns-access.md`, recorder technique).
- Outbound signature: a consumer-side verification of our own delivery fixture passes with the
  documented recipe and fails when one byte of body changes — proving the recipe we publish.

Every refusal emits its stable event code, asserted alongside the refusal
(`knowledge/security/security-logging-detection.md#verification-tests`).
