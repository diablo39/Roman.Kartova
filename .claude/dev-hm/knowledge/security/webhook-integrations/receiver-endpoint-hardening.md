# Webhook integrations — Receiver endpoint hardening

Section of `knowledge/security/webhook-integrations.md`.


- Body size limit at the streaming layer (SEC-120) — a webhook body has a known, small, maximum
  reasonable size; configure it.
- Schema validation after signature verification: the payload parses against the declared
  contract with unknown-field rejection
  (`knowledge/security/control-test-patterns-dataflow.md#parsing-structured-input-outside-the-contract-is-rejected`);
  identifiers in the payload resolve against our records — a verified signature authenticates
  the provider, not the claim that order 9313 belongs to the caller's tenant.
- No secrets in the webhook URL. A random path segment as the only control is the dominated
  option: URLs surface in logs, proxies, and referrers. Legacy carve-out — a provider offering
  no signature mechanism — treats the URL as a rotating secret, adds source filtering where the
  provider publishes addresses, and records the gap in the handoff.
- The endpoint responds minimally: acknowledgement or refusal status, no echo of the payload,
  no processing detail (SEC-072).
