# Resource protection — Input size limits

Section of `knowledge/security/resource-protection.md`.


Request bodies are capped twice: a coarse limit at the edge (gateway or server config) and the
endpoint's own limit, sized to what the operation actually needs — a login body has no reason
to accept megabytes. Uploads have per-file and per-request caps, and field-level maximum
lengths ride the schema contracts of `knowledge/security/api-surface.md`. Compressed input is
bounded on the output side, not the wire size: decompression enforces an absolute decompressed
cap, a compression-ratio cap, and — for archives — an entry-count cap, refusing partway rather
than inflating first and checking later. Large payloads that are legitimate are streamed to
bounded buffers or disk, never accumulated in memory because that was the easy default.

Verification tests: a body one unit over the cap returns 413 and the handler is never entered
(assert the handler spy was not called); an archive whose declared content exceeds the
decompressed cap is refused mid-extraction and temporary space is reclaimed; the streaming path
holds peak memory flat while processing an input larger than memory.
