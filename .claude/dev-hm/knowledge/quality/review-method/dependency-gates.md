# Review method — Dependency gates

Section of `knowledge/quality/review-method.md`.


Backs QUA-060 – QUA-063; the security half of dependency review (lockfiles, confusion, audit,
provenance) is `knowledge/security/supply-chain.md`. For every dependency the diff adds:

1. Justification recorded — the problem it solves and why the standard library or an existing
   dependency was rejected (QUA-060). "Saves twenty lines" does not justify a transitive tree.
2. Alive and licensed — release or commit within 12 months; license recorded as an SPDX
   identifier and checked against the project's license policy (QUA-061; decision procedure in
   the license-policy section below). Absent a policy, compatibility is not decidable: record
   the license and flag the missing policy as a finding rather than guessing.
3. Deterministic — exact versions, manifest and lockfile agree, no wildcard/latest (QUA-062).
4. Generated files untouched by hand — lockfiles and generated clients change only via their
   generators, command recorded (QUA-063).

Weight a dependency by its full cost: transitive count, install scripts, maintenance bus-factor,
upgrade cadence you are signing up for. Prefer boring, widely-used, single-purpose libraries
over frameworks adopted for one feature.
