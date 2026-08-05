# Module and API hygiene

| Sev | Finding | What to look for |
|---|---|---|
| S2 | Deep import into another module's internals | Import bypassing a package's public entry, coupling to private structure. |
| S2 | `import type` missing under verbatim syntax | Type-only import emitted as a runtime import, or a value imported as a type. |
| S3 | Barrel file on a hot path | Large `index.ts` re-export pulling unused modules and hurting tree-shaking. |
| S3 | Public function returns a widened/`any` type | Exported API whose return type erases information callers need. |
