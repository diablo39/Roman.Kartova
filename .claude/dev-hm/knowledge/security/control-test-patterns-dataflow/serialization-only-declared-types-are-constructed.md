# Control-test patterns: parameterized statements, encoding, parsing, telemetry, limits — Serialization: only declared types are constructed

Section of `knowledge/security/control-test-patterns-dataflow.md`.


Our deserializers construct the types we declare, never a type the payload names. Where the
format carries a type discriminator, the accepted set is closed in code; a discriminator
outside it is refused with the deserializer's error, and no undeclared type is constructed.

```java
@Test @Tag("control")
void discriminatorOutsideTheDeclaredSetIsRefused() {
    var json = "{\"type\":\"unregistered\",\"amount\":5}";
    assertThatThrownBy(() -> mapper.readValue(json, PaymentEvent.class))
        .isInstanceOf(InvalidTypeIdException.class);
}
```

```csharp
[TestMethod]
[TestCategory("Control")]
public void UnknownDiscriminatorIsRefused()
{
    var json = """{"$type":"unregistered","amount":5}""";
    Assert.ThrowsExactly<JsonException>(
        () => JsonSerializer.Deserialize<PaymentEvent>(json, _options));
}
```

```python
def test_uploaded_yaml_yields_data_not_objects():
    with pytest.raises(yaml.constructor.ConstructorError):
        parse_uploaded_config("value: !!python/object:x.Y {}")  # loader accepts data tags only
```

| Ecosystem | Type restraint under test |
|---|---|
| TypeScript | `JSON.parse` builds plain data; class-revival mappers restricted to declared classes |
| Python | `yaml.safe_load` for external YAML; external formats deserialize into validated models, never pickle |
| Java | Jackson `@JsonSubTypes` closed set; default typing stays off |
| C# | `[JsonPolymorphic]`/`[JsonDerivedType]` closed set; no legacy binary formatters on external input |
| Rust | serde tagged enums — an unknown tag is a refused variant by construction |
| C++ | an explicit factory registry keyed by allowlisted names |
| Flutter / Dart | `fromJson` factories over a discriminator switch with a refusing default |
