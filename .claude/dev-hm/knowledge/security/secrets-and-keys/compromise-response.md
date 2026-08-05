# Secrets and key management — Compromise response

Section of `knowledge/security/secrets-and-keys.md`.


A secret that ever reached a shared or public place — commit history, a log aggregation system, a
chat message, a crash report — is compromised. Deleting it from where it was seen does not un-leak
it: history, clones, caches, and scrapes retain it. The response is always rotate, not just remove:

1. Rotate or revoke the credential first; then clean up the exposure.
2. Audit use of the credential over the exposure window; treat unexplained use as an incident.
3. Record the event and the rotation in the handoff — location as `file:line` and kind, never the
   value (`knowledge/shared/ground-rules.md`).

Preventive controls in our own pipeline: secret scanning runs in CI and as push protection at the
host, and example/test fixtures use obvious placeholders so scanners stay quiet and humans copy
nothing real (SEC-020).
