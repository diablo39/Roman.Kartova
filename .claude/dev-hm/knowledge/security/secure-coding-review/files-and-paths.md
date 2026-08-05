# Secure code review by vulnerability class — Files and paths

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-041, SEC-045. Path traversal (SEC-045) fails when a path built from external input
reaches the filesystem without canonicalize-then-contain:

```java
// fail SEC-045: ../../etc/passwd walks out of the base directory
Path p = Paths.get(baseDir, request.getParameter("name"));
// pass: canonicalize, then verify containment
Path p = Paths.get(baseDir).resolve(name).normalize();
if (!p.startsWith(Paths.get(baseDir).normalize())) throw new AccessDeniedException(name);
```

Rejecting the substring `..` is not sufficient (encodings, absolute paths, symlinks); the check
is on the canonical result. `normalize()` is lexical and symlink-blind — where symlinks can occur,
resolve first (`toRealPath()`, `realpath`) before checking. For uploads (SEC-041): enforce size and type limits, store under
server-generated names outside the web root or in non-executable storage, and treat the
client-supplied filename as display data only — full pipeline depth and tests: `knowledge/security/file-upload-handling.md`.
