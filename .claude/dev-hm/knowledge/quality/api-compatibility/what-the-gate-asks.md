# API compatibility and versioning — What the gate asks

Section of `knowledge/quality/api-compatibility.md`.


- Does the diff change a published contract? Then: matching contract/schema test change in the
  same diff (QUA-017), configured compatibility check run and recorded (QUA-113), and the
  change classified against the taxonomy — additive, or versioned/coordinated with the break
  stated.
- Removals and renames: deprecation evidence attached (usage telemetry, window elapsed) — an
  undeprecated removal of a used element is an S1 compatibility finding even when every local
  test passes.
- Semantic changes (units, nullability, error shapes): a value-asserting test that would have
  failed before the change, not just a shape check.
- Rolling-deploy safety: would N-1 of this service survive this change live? If not, the
  change needs the expand-contract treatment, not a bigger deploy window.
