# Data classification — Masking

Section of `knowledge/security/data-classification.md`.


Display and export show the minimum the task needs, and the masking is done server-side by one
module — hiding a value in the UI while the API returns it in full is presentation, not
protection.

- Central masking functions per datum type: card numbers show at most the issuer prefix and last
  four digits (the PCI display rule); emails keep the first character and domain; phone numbers
  keep the last digits; national identifiers show a suffix only. One module owns these rules so
  a rule change is one diff, not a hunt across call sites doing ad-hoc string slicing.
- Masked by default: rendering and export paths receive masked values unless the caller holds
  the declared permission for full values. The full-value path is a privileged, audited
  operation with a stated business need — not a query parameter.
- Exports inherit the rule: CSV downloads, reports, support tooling, and admin screens pass
  through the same masking layer as the UI. Exports are where masking is most often forgotten,
  because they are built as "just data" — they are display surfaces with better copy-paste.

Verification: render and export a synthetic Tier-3 fixture and assert the masked shape appears;
request the full value without the permission and assert refusal — the wrong-role pattern from
`knowledge/security/control-test-patterns-access.md`.
