# Control-test patterns: transport, authentication, session, authorization — Session and tokens: revocation is honored server-side

Section of `knowledge/security/control-test-patterns-access.md`.


Every long-lived credential our code issues has a server-side revocation path, and a revoked
credential is refused even though it is otherwise well-formed and unexpired.

```ts
it("control: a revoked session is refused server-side", async () => {
  const cookie = await login(app, user);
  await request(app).post("/logout").set("Cookie", cookie).expect(204);
  await request(app).get("/account").set("Cookie", cookie).expect(401);
});
```

The same shape covers refresh tokens and API keys: revoke through the management path, then
present the old credential and assert refusal. The test must present the credential to the
server — checking that the client forgot it proves nothing about the control.
