# Browser protections — Token storage

Section of `knowledge/security/browser-protections.md`.


Where a browser application keeps its credential is a design decision made once, recorded, and
tied to the rest of this file — any script that runs can read web storage, so storage choices
and script controls (CSP, SRI) stand or fall together. The preference order:

- Cookie session or backend-for-frontend: tokens never reach browser JavaScript. The backend
  holds OAuth tokens and gives the browser only an `HttpOnly`, `Secure`, `__Host-`, `SameSite`
  session cookie. This is the recommended architecture in current IETF guidance for
  browser-based OAuth applications, and our default for anything handling regulated data.
- Tokens held in JavaScript memory only (module closure or worker), short-lived, never
  persisted; silent renewal happens against the token lifecycle rules in
  `knowledge/security/authentication-sessions.md`.
- Web storage (`localStorage`/`sessionStorage`) is a declared decision, not a default: the
  handoff names the mitigations that make it tolerable — short token lifetime, a strict CSP per
  this file, no third-party script in scope, and sender-constrained tokens (DPoP, RFC 9449)
  where the authorization server supports them, so a copied token is not a usable token.

Refresh tokens and other long-lived credentials never sit in web storage regardless of tier.

Verification tests: the cookie-attribute snapshot and the script-invisibility and
storage-scan browser tests in
`knowledge/security/control-test-patterns-browser.md#cookies-and-token-storage`.
