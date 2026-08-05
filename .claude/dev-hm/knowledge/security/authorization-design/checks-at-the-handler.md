# Authorization design — Checks at the handler

Section of `knowledge/security/authorization-design.md`.


Every privileged or role-scoped operation verifies the caller's permission server-side, at the
handler that performs it (SEC-012):

- The decision input is the authenticated principal from the session or verified token — never a
  role, user ID, or tenant field supplied in the request body, query, or an unverified header.
  Anything the caller can write, the caller can improve.
- Hiding a button or route in the client is presentation, not control. The server-side check is
  the control; the client mirrors it for usability.
- Checks are declarative where the stack allows — an annotation, decorator, or route policy
  bound to the central policy module below — so the requirement is visible at the operation and
  greppable across the codebase.
- An operation needs both of its checks: the action check (may this role disable users?) and the
  object check (may they disable this user?). Passing the first does not imply the second.

The wrong-role control test drives the operation with a valid identity lacking the permission
and asserts the project's refusal shape and that the operation had no effect — the regression
tripwire for a dropped or inverted guard.
