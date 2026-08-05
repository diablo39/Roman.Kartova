# Test data management — What the gate asks

Section of `knowledge/quality/test-data-management.md`.


- New tests: is the data constructed or generated (tier 1)? If a production extract is used, do
  the three carve-out conditions hold and does the handoff record them?
- New fixtures: builders over dumps; no real personal data, no real secrets; seeds logged;
  dates relative or clock-fixed.
- Schema or contract diffs: fixtures and seed scripts updated in the same change; fixture
  validation present or a drift risk recorded.
- Data setup honors isolation (QUA-013) and edge-case classes come from the enumerated table
  in `knowledge/quality/test-strategy.md#edge-cases`, not from whatever
  the generator happened to emit.
