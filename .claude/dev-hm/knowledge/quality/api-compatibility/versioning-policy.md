# API compatibility and versioning — Versioning policy

Section of `knowledge/quality/api-compatibility.md`.


- Additive evolution is the default; a new version is the last resort for a break that cannot
  be expressed additively. Every parallel version is a fork you operate, monitor, and secure
  until its last consumer leaves.
- One versioning mechanism per API, chosen once and recorded (ADR-worthy): URI segment, header,
  or media type for HTTP APIs; subject/topic name or schema-registry version for events. The
  mechanism matters less than the consistency; mixing mechanisms breaks caching, routing, and
  documentation at once.
- Version at the surface consumers hold, with major-only granularity: consumers should never
  need to change anything for a compatible provider release.
- Libraries follow semantic versioning: breaking changes to the exported surface require a
  major bump, and the changelog states the break and the migration (QUA-053). What counts as
  exported surface is defined by the taxonomy above applied to the language's visibility rules.
- Event schemas managed through a schema registry declare their compatibility mode (backward,
  forward, full) and let the registry enforce it on registration — the mode is the policy,
  checked mechanically.
