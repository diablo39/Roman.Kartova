# Control-test patterns: parameterized statements, encoding, parsing, telemetry, limits — Data access: identifiers resolve only through the allowlist

Section of `knowledge/security/control-test-patterns-dataflow.md`.


Bind values; map identifiers. Sort keys, column selectors, and table names cannot be bound, so
our code resolves them through a fixed mapping and refuses anything outside it. The sharp probe
is a real column not in the allowlist — the control is a mapping, not a character filter.

```ts
it("control: a sort key outside the allowlist is refused", async () => {
  const res = await request(app).get("/api/orders?sort=internal_notes").set(auth(token));
  expect(res.status).toBe(400);
  expect(queryRecorder.statements).toHaveLength(0);   // refused before any statement ran
});

it("control: every allowlisted sort key resolves", async () => {
  for (const key of ORDER_SORT_KEYS) {
    await request(app).get(`/api/orders?sort=${key}`).set(auth(token)).expect(200);
  }
});
```

The allow-path loop earns its place here: it pins the allowlist as the single source, so adding
a sort key means extending the mapping rather than loosening the check.
