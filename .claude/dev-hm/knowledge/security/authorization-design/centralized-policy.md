# Authorization design — Centralized policy

Section of `knowledge/security/authorization-design.md`.


Authorization rules are computed in one place and asked as questions everywhere else. Handlers
call a policy module — may this principal perform this action on this resource? — instead of
re-deriving role logic inline:

- One implementation of each rule means one place to fix it, one place to test it, and no drift
  between the three handlers that each remembered the rule slightly differently.
- The policy module is pure decision logic — principal, action, resource in; allow or deny
  out — which makes it unit-testable exhaustively, cheaply, at the bottom of the pyramid, while
  the handler-level control tests verify the wiring end to end.
- Refusals come out uniform: the same status shape and the same audit event regardless of which
  handler asked. Every deny emits a structured security event with a stable code, so detection
  can watch the deny rate (`knowledge/security/security-logging-detection.md#event-codes`).
- Where the platform provides a policy engine or shared authorization service, the policy module
  is the adapter to it; the handlers' question stays the same.
- A policy change is a control change: it ships with its updated control tests in the same diff,
  per the mandate in `knowledge/security/control-verification-tests.md`.

Scopes and roles arriving from tokens are inputs to policy, not conclusions: a token scope says
what the client may ask for; policy decides what this principal may do to this resource
(`knowledge/security/authentication-sessions.md`).
