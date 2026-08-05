# Webhook integrations — Inbound: idempotency

Section of `knowledge/security/webhook-integrations.md`.


Webhook delivery is at-least-once everywhere: provider retries, network duplicates, and
in-window replays all present the same verified bytes twice. Processing is keyed on the
delivery (or event) identifier the provider signs:

- A dedupe record — the delivery ID written to a store with a unique constraint, with a TTL
  comfortably longer than the replay window plus the provider's documented retry horizon —
  turns the second presentation into an acknowledged no-op.
- The side effect itself is guarded by the same key where it matters: an insert carrying the
  event ID under a unique constraint, or a compare-and-set, so "processed exactly once" holds
  even when two copies race past the dedupe check concurrently (SEC-111 atomic check-then-act
  applies; the constraint is the atomic form).
- Acknowledge fast, process async: the receiver validates, persists, and returns success; the
  work happens on a queue with its own retry policy. Slow synchronous handlers cause provider
  retries, which manufacture exactly the duplicates this section absorbs, and an unbounded
  burst of deliveries meets a bounded queue instead of unbounded threads
  (`knowledge/security/resource-protection.md#bounded-queues-and-concurrency`).
