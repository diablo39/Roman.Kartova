# Email and notification safety — Links from a configured base

Section of `knowledge/security/email-notification-safety.md`.


Extends SEC-019 from reset links to every link in every message. Any URL in an outbound message
is built from the application's configured public base URL — configuration read at startup —
never from `Host`, `X-Forwarded-Host`, `Origin`, or any other request-derived value. A poisoned
host header otherwise relocates every link in the message, and on a reset or verification mail
that delivers the token to the attacker's server (CWE-640). The rule is absolute for
security-sensitive links and uniform for the rest, because a uniform rule is testable: the
canary test below asserts it across the whole outbox.

Tokens carried in links follow `knowledge/security/authentication-sessions.md`: CSPRNG-generated
(SEC-033), expiring, single-use for reset and verification flows, and stored hashed — the link
is a credential in transit. Messages never carry passwords, session identifiers, or Tier-3
values beyond the notification's recorded purpose
(`knowledge/security/data-classification.md#minimization`); a mailbox is long-term plaintext
storage we do not control, so the message says something happened and links back into the
application, which authenticates and then shows the detail.
