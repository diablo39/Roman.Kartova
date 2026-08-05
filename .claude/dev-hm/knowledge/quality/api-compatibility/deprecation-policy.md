# API compatibility and versioning — Deprecation policy

Section of `knowledge/quality/api-compatibility.md`.


Removal is a process with evidence, not an event:

1. Announce: mark the element deprecated in the schema/IDL and docs, state the replacement and
   the migration, and update the changelog (QUA-053). For HTTP APIs, signal in-band with the
   standard headers — `Deprecation` (RFC 9745) for "this is going away", `Sunset` (RFC 8594)
   for "unresponsive after this date", plus a `Link` to the migration doc — so clients and
   their tooling see it without reading release notes.
2. Measure: instrument usage of the deprecated element per consumer
   (`knowledge/quality/observability.md`). Removal decisions are made on
   observed usage, never on the announcement date alone.
3. Migrate: run old and new side by side through the published window; chase the remaining
   consumers the telemetry names.
4. Remove: only at recorded zero usage, or with the residual consumers' acceptance recorded.
   The removal diff cites the evidence; that record is what turns QUA-017's "contract change
   with matching test change" from a formality into a reviewed decision.

Deprecations that linger past their window are debt and are tracked as such
(`knowledge/quality/maintainability-debt.md#deprecation-hygiene`).
