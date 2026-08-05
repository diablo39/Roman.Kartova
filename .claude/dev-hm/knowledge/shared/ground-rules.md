# Engineering ground rules

These rules apply to every dev-hm agent. They are stated once here; agent files reference this
document instead of repeating them. Cross-references below are relative to `.claude/dev-hm/` in the repository, not to
the session's working directory.

## Secrets

- Do not print secret values (credentials, API keys, tokens, private keys, connection strings
  with passwords) to output, and do not write them into any file — including scratch files,
  logs, examples, and test fixtures.
- When you find a secret in code or history, report its location (`file:line`) and kind, not its
  value. Redact all but enough characters to identify it (e.g. first four).
- Do not echo environment variables that may hold credentials (`env`, `printenv`, `set` dumps).
- Example and template files use placeholders (`<api-key>`, `changeme`), not realistic-looking
  values that scanners flag or humans copy.

## Verify before handoff

- Run the build and the tests relevant to your change before declaring it done. Record the exact
  command and its result in your handoff.
- A check you could not run is reported as "not run" with the reason ("not run: no database
  available"), not claimed as passed. "Should pass" is not verification.
- Code changes carry an oracle self-check (see `knowledge/shared/defense-in-depth.md`) in the
  handoff. Verification evidence and self-check verdicts travel with the diff.
- Do not weaken a check to make it pass: no skipping tests, loosening assertions, or silencing
  warnings to get green. If a check is wrong, report that as a finding instead.
- A change that implements or alters a protective control ships, in the same diff, an automated
  test asserting the control's protective behavior (see `knowledge/security/control-verification-tests.md`).
- Removing or weakening an existing control-verification test requires gate approval, recorded
  in the handoff.

## Condensed reporting

- Cite findings as `file:line` with a one-sentence statement of the problem or evidence. Do not
  paste whole files or long excerpts when a citation does the job.
- Compact errors: the failing test name, the assertion or exception message, and the first frame
  in project code — not full stack traces or full logs.
- Summarize what is healthy in counts ("42 tests pass"); itemize only what needs action.
- State uncertainty explicitly. "Not verified" and "assumed because X" are useful; silent guesses
  are not.

## Files and workspace hygiene

- Temporary and intermediate files go to the session scratchpad directory, not the repository.
- Do not leave report, summary, or plan markdown files in the repository unless the task asked
  for a document as its deliverable.
- Prefer the dedicated Read/Edit/Write tools over shell `cat`/`sed`/`echo` for file work; use
  absolute paths.
- Do not create documentation files proactively; cross-reference existing docs instead.

## Handoff etiquette

A handoff from any agent contains, in order:

1. What changed — file paths, one line each.
2. How it was verified — commands run and results, or explicit "not run" entries with reasons.
3. Oracle self-check verdicts in the shared format (code changes only) — see
   `knowledge/shared/defense-in-depth.md`.
4. Open questions and assumptions the next role must know.

Keep the whole handoff short enough that the receiving agent can read it before the diff.
