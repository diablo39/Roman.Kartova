# Control-test patterns: transport, authentication, session, authorization — Session: a fresh identifier is issued after authentication

Section of `knowledge/security/control-test-patterns-access.md`.


Our session layer rotates the session identifier when the principal authenticates, so an
identifier handed out before login never carries an authenticated context.

```python
def test_session_identifier_rotates_at_login(client):
    client.get("/")                                    # session established pre-auth
    pre_auth_id = client.cookies.get("session")
    client.post("/login", data=VALID_CREDENTIALS)
    assert client.cookies.get("session") != pre_auth_id
    # the pre-auth identifier no longer reaches an authenticated context
    r = fresh_client_with_cookie("session", pre_auth_id).get("/account")
    assert r.status_code in (401, 403)
```

The second half is the load-bearing assertion: rotation without invalidation still honors the
old identifier. Assert both.
