# File upload handling — Content verification

Section of `knowledge/security/file-upload-handling.md`.


Magic bytes prove the file starts like the claimed format; they do not prove it is only that
format. A file can be simultaneously valid in two formats, so the byte check is the gate, not
the verdict. For every accepted type, the content is parsed with a real parser for that format —
an image decoder for images, a structure-validating reader for documents — with the parser's
resource limits set (decompressed pixel count, page count, embedded-object depth; SEC-052
applies to parsers, not only archives). A file the parser cannot fully decode is refused, not
stored "as-is for later".
