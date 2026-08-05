# File upload handling — Acceptance gate

Section of `knowledge/security/file-upload-handling.md`.


The cheapest refusals come first, before the body is buffered:

- Size: a per-request byte cap enforced by the streaming layer, so an oversized upload is cut
  off mid-transfer rather than assembled and then measured. The cap is configuration, and the
  control test derives its probe from the same configuration value
  (`knowledge/security/resource-protection.md#input-size-limits`).
- Count and rate: a cap on files per request and uploads per principal per window, so the
  storage bill and the scan queue are not one loop away from exhaustion
  (`knowledge/security/resource-protection.md#per-principal-fairness`).
- Type allowlist: the accepted set is a short, closed list of types the feature needs — never a
  denylist of known-bad extensions, which loses to every extension it did not anticipate. The
  claimed type must agree three ways: the file extension, the declared `Content-Type`, and the
  magic bytes all map to the same allowlisted entry, or the upload is refused.

The client's `Content-Type` header and filename are claims, not facts. They select the
validation path; they never decide it alone.

```python
# fail SEC-041: trusts the client's declared type
if upload.content_type in ALLOWED_TYPES: save(upload)

# pass: extension, declared type, and magic bytes must agree on one allowlisted entry
kind = detect_magic_bytes(stream.peek(512))          # from file content, not headers
if not (kind in ALLOWED_TYPES
        and claimed_type_matches(upload.content_type, kind)
        and extension_matches(upload.filename, kind)):
    raise ValidationError("file type not accepted")  # emits upload_validation
```
