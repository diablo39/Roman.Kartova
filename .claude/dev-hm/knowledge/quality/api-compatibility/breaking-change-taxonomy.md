# API compatibility and versioning — Breaking-change taxonomy

Section of `knowledge/quality/api-compatibility.md`.


For request/response APIs (HTTP/JSON, gRPC) and published messages/events:

| Change | Compatibility |
|---|---|
| Add an optional field to a response or event | Safe — provided consumers tolerate unknown fields (verify, don't assume) |
| Add an optional request parameter or field with a server-side default | Safe |
| Add a new endpoint, operation, or event type | Safe |
| Add a value to an enum the *provider* sends | Breaking for consumers that switch exhaustively — safe only where consumers documented an unknown-value fallback |
| Add a required request field | Breaking — old clients stop validating |
| Remove or rename any field, endpoint, or event type | Breaking |
| Change a field's type, format, or cardinality (scalar→array, int→string) | Breaking |
| Make an optional field required, or start omitting a previously always-present field | Breaking — nullability is contract |
| Change a field's meaning, unit, timezone basis, or encoding without changing its name | Breaking, and the worst kind — no parser fails, values are silently wrong |
| Tighten validation (reject requests previously accepted) | Breaking for clients that sent them |
| Loosen validation | Safe for clients; review as a security question (SEC-*) |
| Change error status codes or error body shape | Breaking — error handling is contract (QUA-034) |
| Reorder JSON object keys; add response headers | Safe under any reasonable consumer |
| Change protobuf field numbers or Avro field defaults | Breaking at the encoding layer even when names survive |

Two taxonomy notes that decide most disputes: semantic breaks (units, meaning, implicit
ordering guarantees) are breaks even though every schema tool passes them — they are why
contract tests assert values and invariants, not just shapes; and observed behavior becomes
de-facto contract over time — undocumented fields and orderings that consumers can see, they
will depend on, so remove them with the same discipline as documented ones (weigh this by
consumer count; it is a reason to publish less, not to freeze everything forever).

For libraries the same taxonomy applies to the exported API surface, with a second axis: source
compatibility (it still compiles) vs binary/ABI compatibility (it still links) — a distinction
the stack addenda own where it bites (C++, C#, Java).
