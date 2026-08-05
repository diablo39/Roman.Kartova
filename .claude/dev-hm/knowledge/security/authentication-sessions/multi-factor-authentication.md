# Authentication and session integrity — Multi-factor authentication

Section of `knowledge/security/authentication-sessions.md`.


Where the account controls money, production systems, regulated data, or other credentials, a
password alone is not enough evidence. Our code supports a second factor on those surfaces, with
preferences in order:

- Phishing-resistant factors first: passkeys and other WebAuthn platform or roaming
  authenticators bind the authentication to the origin, so the factor cannot be relayed to a
  lookalike. NIST SP 800-63B's assurance levels are the vocabulary for stating what a surface
  requires.
- One-time-password apps where passkeys are not available. SMS is a last resort and never the
  only factor offered — its channel is the weakest of the set.
- Recovery paths are part of the control: a recovery flow weaker than the factor it bypasses is
  the real assurance level of the account. Recovery codes are single-use, stored hashed like
  passwords, and their use is logged as a security event
  (`knowledge/security/security-logging-detection.md`).

Enrollment and factor changes are themselves high-risk operations — they re-verify the existing
factor before accepting a new one, which is the step-up pattern below.
