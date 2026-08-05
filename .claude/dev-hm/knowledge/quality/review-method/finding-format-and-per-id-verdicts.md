# Review method — Finding format and per-ID verdicts

Section of `knowledge/quality/review-method.md`.


One grammar for oracle verdicts and free findings, defined in
`knowledge/shared/defense-in-depth.md`:

```
<oracle-id|finding> <verdict> [<path>:<line>] — <evidence or rationale>
```

Rules that keep reports deterministic and small:

- Every fail cites a location and observable evidence — what is in the code, not what might
  happen. "QUA-041 fail src/client.py:33 — httpx call with no timeout" is complete; "this could
  be slow" is not a finding.
- Passes and n/a's aggregate to summary counts per oracle file; only fails, waivers, and
  findings are itemized.
- Findings carry a severity tag and the quality characteristic they degrade
  (`knowledge/quality/quality-characteristics.md`), e.g.
  `finding S2 src/svc.py:71 — maintainability (testability): clock read inline, untestable`.
- A reviewer proposes, the gate disposes: reviewers may mark `waive-requested` but only the
  owning gate records `waived` (`knowledge/shared/severity-tiers.md#waiver-rules`).
- Suggested fixes are one line and optional; the remediation pointer in the oracle entry
  carries the full guidance.
