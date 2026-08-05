# Resource protection — Parser and query bounds

Section of `knowledge/security/resource-protection.md`.


Parsers of external data run with limits configured, not library defaults assumed: maximum
nesting depth, element and key counts, token and string lengths. XML parsers additionally have
DTDs and entity expansion disabled (SEC-051/SEC-052 in
`knowledge/security/secure-coding-review.md`, deserialization section). Query languages that
let the caller shape the work are bounded by cost, not just size: GraphQL endpoints enforce a
depth cap and a complexity budget computed before resolvers run, and batch endpoints cap the
item count per call — a hundred cheap operations in one envelope is still a hundred operations.

Verification tests: a document nested past the depth cap is refused with a 400 in bounded time;
a query over the complexity budget is refused before any resolver executes (assert resolver
spies uncalled); the batch endpoint refuses a batch one item over its cap.
