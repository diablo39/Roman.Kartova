# Email and notification safety

The controls around the messages our code sends — email, push, SMS, chat notifications — and
the provider callbacks it receives about them. Outbound messages are a rendering surface that
leaves our infrastructure: a header-injection flaw turns one recipient into many, an encoding
miss turns a notification into markup in someone's mail client, and a link built from the
request turns a password reset into credential delivery to an attacker's host. This file backs
SEC-019 (configured link base), SEC-043 (downstream header encoding), and SEC-003 (HTML
rendering) on messaging surfaces; inbound provider callbacks follow
`knowledge/security/webhook-integrations.md`. Test mandate:
`knowledge/security/control-verification-tests.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Header injection refusal | `knowledge/security/email-notification-safety/header-injection-refusal.md` |
| HTML email encoding | `knowledge/security/email-notification-safety/html-email-encoding.md` |
| Links from a configured base | `knowledge/security/email-notification-safety/links-from-a-configured-base.md` |
| Sending discipline | `knowledge/security/email-notification-safety/sending-discipline.md` |
| Inbound provider webhooks | `knowledge/security/email-notification-safety/inbound-provider-webhooks.md` |
| The canary-in-outbox test | `knowledge/security/email-notification-safety/the-canary-in-outbox-test.md` |
| Verification tests | `knowledge/security/email-notification-safety/verification-tests.md` |
