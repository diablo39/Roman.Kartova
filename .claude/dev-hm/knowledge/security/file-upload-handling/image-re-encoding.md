# File upload handling — Image re-encoding

Section of `knowledge/security/file-upload-handling.md`.


For raster images, verification is completed by transformation: decode the upload and re-encode
it to a fresh file in the target format. What survives is pixels; anything that rode along in
unused segments, trailing bytes, or dual-format tricks does not survive a decode–re-encode
cycle. Re-encoding also strips metadata — EXIF blocks carry GPS coordinates and device
identifiers, which is personal data the uploader rarely intends to publish
(`knowledge/security/data-classification.md#minimization`); stripping is the default, and any
metadata the feature genuinely needs is extracted into fields first. The dominated alternative —
storing the original bytes after inspection — is acceptable only where the feature requires the
original artifact (a document vault, evidence storage); that carve-out is declared in the
handoff, and such files are never served inline (see the serving rules below).
