# Data classification — Residency

Section of `knowledge/security/data-classification.md`.


Where data lives and is processed is a control whenever law or contract constrains it:

- The project declares its residency constraint once; every new store and processing location
  for Tier-3 data states its region and matches the constraint. No declared constraint means
  this section records "none declared", not silence.
- Regions are pinned in infrastructure-as-code, not left to console or provider defaults, and
  replication targets and failover regions honor the same constraint as the primary.
- Telemetry and third-party processors count as processing locations: crash reporting, analytics,
  and support tooling that export data to another region are residency decisions. The telemetry
  controls above keep Tier-3 out of those flows, which resolves most of the exposure; whatever
  legitimately remains is declared like any other processing location.

Verification: an infrastructure policy test asserts the declared regions of storage and
processing resources match the constraint — deny-by-policy in the platform where available, a
fixture test over the infrastructure code otherwise; not applicable when the project declares no
constraint.
