# API surface protection — Pagination and result caps

Section of `knowledge/security/api-surface.md`.


Every collection endpoint has a default page size and a hard maximum, both enforced server-side.
A request above the maximum is clamped or refused (pick one per project and state it in the API
contract); a request with no parameters returns at most the default page. An endpoint that can
return an unbounded result set is a single-request availability failure regardless of intent
(CWE-770). Prefer cursor pagination for large or hot collections — offset pagination makes deep
pages progressively more expensive, which turns paging itself into a work amplifier. Where the
count of matching rows is part of the response, bound that query too or make it approximate.

Verification tests: `limit` above the maximum yields the declared behavior (clamp or 400), never
the requested size; the no-parameter request returns at most the default page; the response
item count never exceeds the cap for any input combination the test sweeps.
