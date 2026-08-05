# Escalation and return flow

- Gate result `fail` on any S0, or on an S1 without an accepted waiver, returns the work package
  to the owning developer. The return message is the verdict lines — IDs, locations, remediation
  pointers from the oracle — not prose re-explanation.
- The developer fixes, re-runs the self-check, and re-enters at layer 2. Re-review scopes to the
  changed entries plus anything the fix touched.
- Reviewer/developer disagreement on a verdict is not argued in place: both positions go in the
  report and the owning gate adjudicates.
- A reviewer who cannot decide an entry (needs runtime evidence, missing context) reports it as
  `fail` with "not verifiable from the diff — needs X", never as `pass`. Unverifiable is not
  passing (see `knowledge/shared/ground-rules.md`).
- Repeated returns (more than two round trips on the same IDs) escalate to the orchestrating
  session or the human with a summary of positions.
