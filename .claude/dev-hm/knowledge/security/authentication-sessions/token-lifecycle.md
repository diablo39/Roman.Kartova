# Authentication and session integrity — Token lifecycle

Section of `knowledge/security/authentication-sessions.md`.


Where our code issues or accepts tokens, every token carries its own boundaries and every
verifier enforces all of them:

- Access tokens are short-lived — minutes to an hour, not days. A short lifetime is the base
  revocation control: it bounds how long a lost token matters, and everything longer-lived gets
  an explicit revocation path.
- Verification checks the full set on every request: signature against a pinned algorithm
  allowlist (SEC-017), issuer, audience, expiry, and not-before. Audience checks stop a token
  minted for one service being replayed at another; an ID token is proof of authentication for
  the client, never an access credential for an API.
- Every issued token carries the ID of the key that signed it, so signing-key rotation is
  routine (`knowledge/security/secrets-and-keys.md`). Verifiers fetch keys by ID from the
  issuer's published set and refuse unknown IDs.
- Refresh tokens rotate on every use, and the old value is invalidated at rotation. A rotated
  refresh token presented again signals that two parties hold the same credential; the response
  is to revoke the whole token family, per the OAuth security best current practice (RFC 9700).

```python
# fail: verifier trusts whatever the token claims about itself
claims = jwt.decode(token, key, options={"verify_aud": False})

# pass: algorithm pinned, audience and issuer required, key selected by ID
claims = jwt.decode(token, key_for(header.kid), algorithms=["ES256"],
                    audience="orders-api", issuer=settings.issuer)
```

One control test per rejected class keeps the verification options pinned by behavior: the
fixture mints a token violating exactly one rule — expired, wrong audience, unknown key ID,
unpinned algorithm — and asserts refusal.
