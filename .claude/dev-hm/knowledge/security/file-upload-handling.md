# File upload handling

The upload pipeline as a chain of controls our code provides: refuse what we do not accept,
verify what we accept, transform it into something inert, store it where it cannot execute or
escape, and authorize every write and read of it. This file is the depth behind SEC-041 (upload
constraints); the pipeline also backs SEC-045 (path containment), SEC-052 (archive limits),
SEC-003 (stored XSS via active content), SEC-011 (per-object authorization), and SEC-120 (size
limits) on upload surfaces. The diff-review quick reference is
`knowledge/security/secure-coding-review/files-and-paths.md`; the test mandate and marker
convention are in `knowledge/security/control-verification-tests.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Pipeline overview | `knowledge/security/file-upload-handling/pipeline-overview.md` |
| Acceptance gate | `knowledge/security/file-upload-handling/acceptance-gate.md` |
| Content verification | `knowledge/security/file-upload-handling/content-verification.md` |
| Image re-encoding | `knowledge/security/file-upload-handling/image-re-encoding.md` |
| Names and storage | `knowledge/security/file-upload-handling/names-and-storage.md` |
| Per-object authorization, both directions | `knowledge/security/file-upload-handling/per-object-authorization-both-directions.md` |
| Active content: SVG and HTML | `knowledge/security/file-upload-handling/active-content-svg-and-html.md` |
| Archives: containment and bounds | `knowledge/security/file-upload-handling/archives-containment-and-bounds.md` |
| Verification tests | `knowledge/security/file-upload-handling/verification-tests.md` |
