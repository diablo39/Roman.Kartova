# Secure code review by vulnerability class — Authentication and sessions

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-013 – SEC-019. Password handling uses a memory-hard KDF — argon2id (preferred) or
scrypt — via a vetted library; general-purpose hashes fail even when salted. bcrypt and PBKDF2 are
not memory-hard: legacy-only, passing when they verify a store that pre-exists the diff and the
login path rehashes to a memory-hard KDF — never for a new store. For JWTs (SEC-017), the
verification call must pin the algorithm and reject `none`:

```python
# fail SEC-017: not pinned (legacy PyJWT <2.0 read the header's alg; >=2.0 raises here)
jwt.decode(token, key, algorithms=None)
# pass: pinned algorithm, expiry enforced
jwt.decode(token, key, algorithms=["RS256"], options={"require": ["exp"]})
```

Check session lifecycle in the diff: regenerate the session ID at login (fixation), invalidate
server-side at logout, expiry on every token. For SEC-015, new credential endpoints (login,
reset, OTP) need a named anti-automation control — in code or explicitly attributed to the
gateway. For SEC-016, OAuth/OIDC code must exact-match redirect URIs against registered values
(no wildcards, no prefix match) and carry `state` or PKCE on authorization-code flows. Session
cookies set by the diff carry `Secure` and `HttpOnly` (SEC-018), and password-reset/verification
links come from a configured base URL, never from the request `Host`/`X-Forwarded-Host` header —
a poisoned host delivers the reset token to the attacker (SEC-019, CWE-640).
