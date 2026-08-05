# Webhook integrations

The controls on webhooks in both directions: callbacks we receive from providers and partners
(inbound), and event notifications we deliver to customer-configured endpoints (outbound). An
inbound webhook is an unauthenticated-by-default endpoint taking orders from the internet; the
three controls that make it trustworthy — signature verification, a freshness window, and
idempotent processing — are cheap, deterministic, and each has a fails-when-removed test.
Outbound webhooks add two duties: sign what we send, and treat the customer-supplied
destination as an SSRF vector. Backs SEC-034 (constant-time comparison), SEC-040 (boundary
validation), SEC-090 (outbound URL control), SEC-162 (declared auth opt-out). Test mandate:
`knowledge/security/control-verification-tests.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Trust model | `knowledge/security/webhook-integrations/trust-model.md` |
| Inbound: signature verification | `knowledge/security/webhook-integrations/inbound-signature-verification.md` |
| Inbound: replay window | `knowledge/security/webhook-integrations/inbound-replay-window.md` |
| Inbound: idempotency | `knowledge/security/webhook-integrations/inbound-idempotency.md` |
| Receiver endpoint hardening | `knowledge/security/webhook-integrations/receiver-endpoint-hardening.md` |
| Outbound: signing what we send | `knowledge/security/webhook-integrations/outbound-signing-what-we-send.md` |
| Outbound: destination control | `knowledge/security/webhook-integrations/outbound-destination-control.md` |
| Verification tests | `knowledge/security/webhook-integrations/verification-tests.md` |
