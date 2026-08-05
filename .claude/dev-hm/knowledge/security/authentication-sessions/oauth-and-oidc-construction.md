# Authentication and session integrity — OAuth and OIDC construction

Section of `knowledge/security/authentication-sessions.md`.


When our code is an OAuth client, resource server, or authorization server integration, the flow
is constructed from the current consolidated guidance (RFC 9700, carried into the OAuth 2.1
consolidation) rather than the permissive historical surface:

- Authorization-code flow with PKCE for every client type — browser, native, and confidential
  alike. No implicit grant, no resource-owner-password grant in new code.
- Redirect URIs exact-match registered values (SEC-016); `state` binds the response to the
  request; OIDC `nonce` binds the ID token to the client session.
- The resource server validates tokens itself — signature, issuer, audience, expiry — and maps
  scopes and claims to its own authorization model
  (`knowledge/security/authorization-design.md`); scope possession is not object ownership.
- Sender-constrained access tokens (DPoP or mTLS-bound) where the platform supports them raise
  high-value APIs from "whoever holds it" to "whoever holds it and can prove the binding".

Which storage a browser client uses for its tokens, and the tradeoffs, are in
`knowledge/security/browser-protections.md#token-storage`.
