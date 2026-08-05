# Authentication and session integrity — Verification tests

Section of `knowledge/security/authentication-sessions.md`.


The control tests this file's controls map to, per the mandate in
`knowledge/security/control-verification-tests.md`:

- Session rotation: fresh identifier at login; the pre-login identifier refused afterwards.
- Expiry: idle and absolute bounds enforced server-side, tested with an injected clock.
- Logout and revocation: the revoked session, refresh token, or API key presented to the server
  is refused; rotated refresh-token reuse revokes the family.
- Token verification: one refusal test per rejected class — expired, wrong audience, unknown key
  ID, unpinned algorithm.
- Step-up: the high-risk operation refused with a stale authentication event, permitted with a
  fresh one.
- Credential storage fixture: stored hashes carry the expected memory-hard KDF format (legacy
  formats only on records not yet rehashed); the login error shape is identical for unknown
  accounts and wrong passwords.
