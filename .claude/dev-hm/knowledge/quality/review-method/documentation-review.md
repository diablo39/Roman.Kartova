# Review method — Documentation review

Section of `knowledge/quality/review-method.md`.


Backs QUA-050, QUA-051, QUA-053. Documentation is reviewed as part of the change, not as an
afterthought: new public API carries doc comments stating purpose, parameters, and error
behavior (QUA-050) — the error behavior clause is the one most often missing and most valuable;
new config keys, env vars, and flags appear wherever the repo documents them, with defaults and
units (QUA-051); user-visible behavior changes update the changelog/user docs when the repo
maintains them (QUA-053). Comments in code explain why, not what — a comment restating the code
is a smell, a comment explaining a non-obvious constraint is gold. Structural decisions get
ADRs (QUA-052, `knowledge/architecture/adr-practice.md`); doc-type placement follows
`knowledge/architecture/diataxis.md`.
