# Review method — Lint and type gates

Section of `knowledge/quality/review-method.md`.


QUA-025 makes the configured linter and formatter authoritative: zero errors on changed files
(S1), zero new warnings (QUA-027, S2), commands recorded. Review implications: style debates end
at the linter config — a style
preference not encoded there is S3 at most, and the fix is a config PR, not a review comment
(never send a human, or an agent, to do a linter's job). Type checkers count as part of the
build (QUA-001): new `any`/untyped escapes, suppressions (`# type: ignore`, `@ts-ignore`,
`@SuppressWarnings`) added without a stated reason are findings — each suppression is a small
waiver and needs the same one-line rationale. Do not weaken lint/type config in the same diff
as the code it would flag; config loosening is its own reviewed change.
