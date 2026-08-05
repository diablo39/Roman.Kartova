# API design: contracts, versioning, compatibility, spec artifacts — Versioning and deprecation

Section of `knowledge/architecture/api-design.md`.


Compatibility first, versions second: a new version number is the admission that compatible
evolution failed, and it forks docs, SDKs, tests, and support for years. Most changes —
additive fields, new endpoints, new event types — need no version bump at all.

When a break is genuinely unavoidable:

| Style | Mechanism (pragmatic default first) |
|---|---|
| REST | Major version in the path (`/v2/orders`) — visible, cache-friendly, trivially routable. Header/media-type versioning is cleaner in theory; choose it only when the team can enforce the required tooling discipline |
| gRPC | New proto package (`billing.v2`), both served during migration |
| Events | New subject version token (`orders.v2.*`); run both until consumers move |

Version majors only. Minor/patch versioning of a contract signals compatibility rules aren't
trusted; fix the rules instead. Keep at most two majors live — each live major multiplies test
and support surface.

Deprecation is a protocol, not an announcement:

1. Mark it in the spec (`deprecated: true` in OpenAPI/AsyncAPI, `[deprecated = true]` in proto)
   so it surfaces in generated docs and IDEs.
2. Signal it in responses: `Deprecation` header (RFC 9745; structured-field date, Unix time)
   from the moment of deprecation, `Sunset` header (RFC 8594; HTTP-date) once the removal date
   is committed, plus a `Link` to the migration guide.
3. Measure before removing: per-consumer telemetry on deprecated-surface usage; chase the last
   consumers actively. Removal without usage data is a production incident scheduled in
   advance.
4. Remove on the announced date; a sunset date that slips twice teaches consumers to ignore
   sunset dates.
