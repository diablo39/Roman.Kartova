# Browser protections — Request-forgery defenses

Section of `knowledge/security/browser-protections.md`.


Scope: endpoints authenticated by cookies, where the browser attaches credentials
automatically. APIs authenticated only by an explicit header (Bearer) are structurally exempt —
record that as the n/a reason rather than adding dead controls. For cookie-authenticated state
changes our code layers three defenses, cheapest first:

- Session cookies carry `SameSite=Lax` (or `Strict` where the flows allow it) and the `__Host-`
  prefix, which locks `Secure`, path `/`, and no `Domain` attribute. `Lax` only helps if safe
  methods never mutate state — that invariant is part of this control.
- State-changing requests are refused when the browser declares a cross-site initiator: the
  request-forgery gate requires `Sec-Fetch-Site: same-origin` (or `none`, for direct
  navigation). Every current browser sends Fetch Metadata; requests without the header fall
  through to the token check rather than being trusted.
- The framework's synchronizer-token mechanism stays enabled on form- and cookie-driven
  applications; a request without the expected token is a 403 before handler logic.

Content-type restraint on the server closes the no-preflight window that would let a cross-site
page submit JSON-shaped bodies as plain text (`knowledge/security/api-surface.md`, method and
content-type restraint). CORS configuration is the server-side counterpart and lives there too.

Verification tests: both directions — cross-site-shaped refused with state unchanged,
same-origin with a valid token allowed — in
`knowledge/security/control-test-patterns-browser.md#request-forgery-cross-site-refused-same-origin-allowed`.
