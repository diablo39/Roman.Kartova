# Control-test patterns: parameterized statements, encoding, parsing, telemetry, limits — Parsing: structured input outside the contract is rejected

Section of `knowledge/security/control-test-patterns-dataflow.md`.


Our parsers validate structured input against a declared contract and reject what we do not
accept — unknown fields, wrong types, missing required members — before any handler logic or
sink sees the value. Assert the specific rejection and that the side effect is absent.

```rust
#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct CreateOrder { sku: String, quantity: u32 }   // the production request type

#[tokio::test]
async fn control_unknown_field_is_rejected_before_the_handler() {
    let app = build_app(recording_state());
    let res = app.oneshot(post_json("/api/orders",
        r#"{"sku":"a-1","quantity":2,"priority":"vip"}"#)).await.unwrap();
    assert_eq!(res.status(), StatusCode::UNPROCESSABLE_ENTITY);
    assert!(recorded_orders().is_empty());          // the handler never ran
}
```

```dart
test('control: a wrong-typed field is rejected by the parser', () {
  final raw = jsonDecode('{"sku":"a-1","quantity":"two"}');
  expect(() => CreateOrder.fromJson(raw),
      throwsA(isA<CheckedFromJsonException>()));
}, tags: ['control']);
```

| Ecosystem | Rejection mechanism under test |
|---|---|
| TypeScript | zod schemas with `.strict()` — parse at the boundary, never cast |
| Python | pydantic models with `extra="forbid"` and strict field types |
| Java | Jackson `FAIL_ON_UNKNOWN_PROPERTIES` plus Bean Validation on the bound object |
| C# | `JsonUnmappedMemberHandling.Disallow` plus model validation |
| Rust | serde typed fields with `deny_unknown_fields` |
| C++ | schema validation before any object construction |
| Flutter / Dart | json_serializable with `checked: true` and `disallowUnrecognizedKeys: true` |

One test per rejected class — unknown field, wrong type, missing member, out-of-range value —
each violating exactly one rule, so the contract stays pinned rule by rule.
