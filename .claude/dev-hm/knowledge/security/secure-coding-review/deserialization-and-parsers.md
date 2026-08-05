# Secure code review by vulnerability class — Deserialization and parsers

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-050 – SEC-052. Language-native object deserialization of external input is code
execution by design — `pickle.loads`, `ObjectInputStream.readObject`, `BinaryFormatter`,
`yaml.load` without SafeLoader, Jackson default typing on untrusted input all fail SEC-050,
whatever validation surrounds them. Cross boundaries with data-only formats and schema validation:

```python
# fail SEC-050
obj = pickle.loads(request.body)
# pass: data-only format, validated shape
obj = OrderSchema.model_validate_json(request.body)
```

XML parsers touching external input need DTDs and external entities disabled before parsing
(SEC-051). All parsers of external data enforce size, depth, and entity limits, and archive
extraction caps decompressed size and entry count — zip bombs and billion-laughs are
availability failures with one-line fixes (SEC-052).
