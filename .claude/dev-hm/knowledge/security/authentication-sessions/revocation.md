# Authentication and session integrity — Revocation

Section of `knowledge/security/authentication-sessions.md`.


Every long-lived credential our code issues has a server-side path to make it stop working
before its natural expiry. A bearer credential we cannot revoke is a promise we cannot take
back. The path per credential type:

| Credential | Revocation path |
|---|---|
| Server-side session | Delete or flag the session record; enforcement is the store lookup already on every request |
| Refresh token | Store-backed: rotation invalidates the predecessor; explicit revocation removes the family |
| Access token | Short expiry bounds the window; high-value surfaces add a denylist checked at the enforcement point, entries held until natural expiry |
| API key, device token | Store lookup on use, or a versioned-invalidation claim: the token carries a version, the store holds the current one, mismatch is refusal |

Design rules that keep revocation real:

- Revocation is enforced where authorization is enforced — the resource server or middleware —
  not only at the issuer. A revoked credential presented to any of our services is refused.
- Caching a revocation check is a latency decision with a security budget: the cache TTL is the
  maximum time a revoked credential keeps working. State the budget; keep it short for
  administrative and Tier-3 surfaces (`knowledge/security/data-classification.md`).
- Security events cascade into revocation: password change, reported compromise, and "log out
  everywhere" bump the principal's token version or clear their sessions — one write that
  invalidates every outstanding credential.
- When the revocation store is unreachable, sensitive operations fail closed (SEC-071); the
  short access-token lifetime keeps the blast radius of a deny-outage acceptable.

The control test revokes through the management path, then presents the otherwise-valid
credential to the server and asserts refusal (`knowledge/security/control-test-patterns-access.md`).
