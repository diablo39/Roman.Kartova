# LLM feature controls — Retrieval scoping

Section of `knowledge/security/llm-feature-controls.md`.


Retrieval runs with the requesting caller's authorization, not the feature's. Our retrieval layer
filters at query time using permission metadata bound to the authenticated context — tenant
partition keys, ACL filters, per-collection scoping — so a caller cannot receive, through the
model, a document they could not read directly. The filter value comes from the session, never
from model output or request payload (`knowledge/security/authorization-design.md` tenant
isolation applies to vector stores exactly as to SQL). Writes are scoped too: a shared corpus
that many callers' features retrieve from accepts documents only through a controlled ingestion
path, because a writable shared corpus is an instruction-delivery channel into everyone else's
context.

Verification tests: a query under tenant B returns nothing from tenant A's partition; a document
ACL-restricted to caller A never appears in the assembled context for caller B — asserted at the
retrieval layer with a stubbed model, so it is deterministic; the ingestion path refuses writes
from principals without the corpus-writer permission.
