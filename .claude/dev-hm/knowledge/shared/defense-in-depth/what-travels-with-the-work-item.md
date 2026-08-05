# What travels with the work item

- Layer 1 → 2: diff, verification evidence (commands + results), self-check `oracle-verdicts` block.
- Layer 2 → 3: review report (`oracle-verdicts` block + findings), plus the unchanged layer-1
  block for comparison. Pass the two blocks, not the two full reports.
- Layer 3 → done: gate verdict table including waiver decisions. This is the record the calling
  role reads as its progress signal.

Pass verdict blocks between layers, never re-paste the diff or file contents a layer can read for
itself.
