# Review method — Reviewing the review

Section of `knowledge/quality/review-method.md`.


Gate agents audit reviews, not just code. Signals of a weak review to re-run rather than trust:
verdict tables with implausibly uniform passes on a large diff; findings without locations;
oracle IDs cited for judgment calls; "looks good" on files the reviewer could not have executed;
disagreement between developer self-check and reviewer verdicts left unflagged (the discrepancy
is itself a finding, per `knowledge/shared/defense-in-depth.md`).
