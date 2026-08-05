# Webhook integrations — Inbound: replay window

Section of `knowledge/security/webhook-integrations.md`.


A verified delivery is a verified delivery forever unless freshness is part of what was signed.
The provider includes the send timestamp inside the signed content; the receiver rejects
deliveries whose timestamp falls outside a short tolerance window — the widely adopted
convention is around five minutes, sized to cover clock skew and provider retry latency, and
taken from configuration so the control test can probe both sides of it. Two requirements make
the window real:

- The timestamp must be covered by the signature. A freshness check on an unsigned header is
  decoration — the header travels with the replay.
- Receiver clocks are NTP-disciplined; the tolerance absorbs skew, it does not excuse
  unsynchronized hosts.

The window shrinks the replay problem to minutes; idempotency (next section) removes what is
left, including provider retries inside the window.
