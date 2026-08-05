# Control-test patterns: transport, authentication, session, authorization — Authorization: the wrong role is refused

Section of `knowledge/security/control-test-patterns-access.md`.


Our handlers check the authenticated principal's permissions; possession of a valid identity
without the permission is refused, and the operation has no effect.

```python
def test_disable_refuses_reader_role(client, reader_token, db):
    r = client.post("/api/users/42/disable", headers=auth(reader_token))
    assert r.status_code == 403
    assert db.get(User, 42).disabled is False          # the operation had no effect
```

```java
@Test
void disableUser_withReaderRole_isRefused() throws Exception {
    mvc.perform(post("/api/users/42/disable")
            .with(jwt().authorities(new SimpleGrantedAuthority("ROLE_READER"))))
       .andExpect(status().isForbidden());
    assertThat(users.findById(42L).orElseThrow().isDisabled()).isFalse();
}
```

```ts
it("control: disable is refused for the reader role", async () => {
  const res = await request(app).post("/api/users/42/disable").set(auth(readerToken));
  expect(res.status).toBe(403);
  expect(await users.get(42)).toMatchObject({ disabled: false });
});
```

This test is the regression tripwire for the guard itself: a later change that drops or inverts
the role check makes the request succeed and the test fail. Pair it with the unauthenticated
variant (no credentials, expect 401) so both halves of the decision are pinned.

Deny-by-default wiring gets its own test — new routes must land behind the auth middleware
without anyone remembering to add them:

```python
def test_every_route_registers_behind_auth_middleware(app):
    exempt = {"/health", "/metrics"}                   # declared opt-outs, reviewed at the gate
    assert set(routes_without_auth(app)) <= exempt     # router introspection helper
```
