# Control-test patterns: parameterized statements, encoding, parsing, telemetry, limits — Resource limits: input past the cap is refused

Section of `knowledge/security/control-test-patterns-dataflow.md`.


Our boundaries refuse input beyond the configured size, depth, or count with the limit error,
before the expensive work runs (limit design: `knowledge/security/resource-protection.md`).
Pin the boundary from both sides, and derive the probe from the same configuration value the
control reads, so the test moves when the limit moves.

```cpp
TEST_CASE("nesting past the configured depth is refused", "[control]") {
    REQUIRE(parse_request_body(nested_json(config::max_depth)).has_value());
    auto over = parse_request_body(nested_json(config::max_depth + 1));
    REQUIRE(over.error() == ParseError::DepthLimitExceeded);
    REQUIRE(handler_invocations() == 0);            // refused before any work was queued
}
```

```ts
it("control: a body one byte past the cap is refused", async () => {
  const cap = config.maxBodyBytes;
  await request(app).post("/api/import").send("x".repeat(cap + 1)).expect(413);
  expect(importJobs.started).toBe(0);               // no work was queued
  await request(app).post("/api/import").send("x".repeat(cap)).expect(202);
});
```

The same shape covers every bound our code declares: body size, decompressed size where our
code inflates input, nesting depth, element and page-size counts, and per-principal request
rates (the over-rate request receives the throttling status; the handler count stays flat).
