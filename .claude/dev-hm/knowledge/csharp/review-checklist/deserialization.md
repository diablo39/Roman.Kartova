# C# review checklist — Deserialization {#deserialization}

Section of `knowledge/csharp/review-checklist.md`.


`BinaryFormatter` is removed/unsafe — never use it. For JSON, do not enable polymorphic
deserialization that resolves arbitrary types from untrusted payloads; constrain with a known
type-discriminator allowlist. Validate deserialized objects before use.
