# Email and notification safety — Inbound provider webhooks

Section of `knowledge/security/email-notification-safety.md`.


Delivery receipts, bounce notifications, and inbound-parse callbacks are external input from
the internet, whatever provider name is on them. The full receiver discipline lives in
`knowledge/security/webhook-integrations.md`: verify the provider's signature before parsing
(schemes differ per provider — HMAC over the raw body, HMAC over timestamp-plus-token, or an
asymmetric signature; implement exactly the documented scheme and pin its key), enforce the
freshness window, deduplicate on the provider's event identifier. Specific to email callbacks:

- An inbound-parse payload is a parsed email: sender, subject, and body are attacker-chosen
  strings (encode on render, bind on query), and attachments enter the full pipeline of
  `knowledge/security/file-upload-handling.md` as if uploaded by an anonymous user.
- Bounce and complaint handlers mutate delivery state; they verify that the referenced message
  identifier is one we sent before acting, so a forged bounce cannot suppress or unsubscribe an
  arbitrary recipient.
