# Authentication and session integrity

The controls that keep identity trustworthy from login to logout. The diff-review quick
reference lives in `knowledge/security/secure-coding-review/authentication-and-sessions.md`
(SEC-013 – SEC-019); this file covers the lifecycle design behind those entries — how sessions
and tokens are issued, refreshed, verified, and ended, and the tests that prove each control
holds. Runnable test patterns are in `knowledge/security/control-test-patterns-access.md`.
Cookie attributes, CSRF defenses, and browser token storage are in
`knowledge/security/browser-protections.md`; rate limits on credential endpoints are in
`knowledge/security/api-surface.md`; signing-key custody and rotation are in
`knowledge/security/secrets-and-keys.md`. NIST SP 800-63B supplies the reference vocabulary for
authenticator and session assurance.

An authentication result is a fact with a lifetime. The controls below exist so that the fact is
established honestly, carried in a credential that cannot outlive its welcome, and withdrawn
server-side the moment we decide it no longer holds.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Session lifecycle | `knowledge/security/authentication-sessions/session-lifecycle.md` |
| Token lifecycle | `knowledge/security/authentication-sessions/token-lifecycle.md` |
| Revocation | `knowledge/security/authentication-sessions/revocation.md` |
| Multi-factor authentication | `knowledge/security/authentication-sessions/multi-factor-authentication.md` |
| Step-up | `knowledge/security/authentication-sessions/step-up.md` |
| OAuth and OIDC construction | `knowledge/security/authentication-sessions/oauth-and-oidc-construction.md` |
| Credential storage | `knowledge/security/authentication-sessions/credential-storage.md` |
| Verification tests | `knowledge/security/authentication-sessions/verification-tests.md` |
