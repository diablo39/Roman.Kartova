# Email and notification safety — Sending discipline

Section of `knowledge/security/email-notification-safety.md`.


- One sending path: a single mailer module owns provider credentials, template rendering, and
  the controls above — the messaging counterpart of
  `knowledge/security/secrets-and-keys.md#one-access-path`. Handlers request "send template X
  to principal Y with fields Z"; they never assemble messages.
- Rate and recipient bounds: per-principal and per-recipient send caps
  (`knowledge/security/resource-protection.md#per-principal-fairness`), so a triggerable
  notification cannot be turned into a flooding or bombing primitive against a victim's
  mailbox; triggered sends to addresses the actor does not own (invites, shares) get the
  tightest caps.
- Sender authentication (SPF, DKIM, DMARC alignment) is platform configuration owned by the
  domain, not per-diff code — but a diff that adds a new sending domain or provider names who
  owns that configuration in the handoff.
- Send events are security events where the message is security-relevant (reset, verification,
  invite): emitted with actor, template, and recipient reference — masked per
  `knowledge/security/security-logging-detection.md#regulated-data-stays-out` — never the body.
