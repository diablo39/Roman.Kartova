# Authentication and session integrity — Step-up

Section of `knowledge/security/authentication-sessions.md`.


Some operations warrant more than a live session: exporting Tier-3 data, changing credentials or
factors, destructive administrative actions. For those, our code verifies a recent
authentication event — or a second factor — at the operation, regardless of how old the session
is:

- The handler checks authentication freshness server-side: an authentication-time claim
  (`auth_time`, or the session's authenticated-at timestamp) compared against the operation's
  freshness window, or an authentication-context claim (`acr`) stating the factor used.
- A stale authentication gets a re-prompt flow, then the operation proceeds; the check sits at
  the handler, so no client shortcut skips it.
- The set of step-up operations is declared in the work package, and when the platform's
  identity layer provides the mechanism, the handoff names it — the check at our handler remains
  either way.

The control test performs the high-risk operation with a valid session whose authentication
event is older than the freshness window and asserts refusal with the re-authentication
signal — then repeats it fresh and asserts success, pinning both sides of the window.
