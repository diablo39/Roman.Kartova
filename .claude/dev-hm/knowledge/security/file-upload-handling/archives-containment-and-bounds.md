# File upload handling — Archives: containment and bounds

Section of `knowledge/security/file-upload-handling.md`.


Accepting an archive means accepting every entry in it, so extraction is its own boundary:

- Containment: each entry's target path is resolved against the extraction root and verified to
  stay inside it before any write — entry names are external input, and relative traversal in
  an entry name (the zip-slip pattern) otherwise writes outside the tree (SEC-045). Symbolic
  links and hard links in entries are refused unless the feature explicitly requires them, and
  then targets are verified contained after resolution.
- Bounds: a cap on total decompressed size, per-entry size, entry count, and nesting depth
  (archives inside archives), enforced during extraction, not after (SEC-052). The
  decompression ratio is the tell the limits guard against; the caps come from configuration
  the control tests share.
- Each extracted file then enters the pipeline above as if uploaded individually — type
  allowlist, verification, generated names. An archive is a transport, not a trust grant.

```java
// pass SEC-045: containment verified per entry before any write
Path target = extractRoot.resolve(entry.getName()).normalize();
if (!target.startsWith(extractRoot)) throw new ValidationException(entry.getName());
```
