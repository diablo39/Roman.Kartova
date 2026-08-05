# Oracle addendum: Python

Python-specific, deterministic gate rules layered on the core `oracles/security-oracle.md`
(SEC-*) and `oracles/quality-oracle.md` (QUA-*). Apply these to `.py` changes. Test files
are exempt where noted (assert usage, some subprocess). Where a core entry and an addendum
entry cover the same defect, the stricter severity and verdict govern.

## Security (SEC-PY)

| ID | Check | Pass criterion | Sev | Remediation |
|---|---|---|---|---|
| SEC-PY-001 | Dynamic code execution on external input | Zero `eval(`, `exec(`, or `compile(` calls whose argument derives from a parameter, request, file, or env value. | S0 | knowledge/python/review-checklist/security-review-hooks.md#injection-and-untrusted-input |
| SEC-PY-002 | Shell command injection | Every `subprocess.*` / `os.system` / `os.popen` call uses an argument list with `shell=False` (the default for `subprocess`); no `shell=True` with a string built from external input. | S0 | knowledge/python/review-checklist/security-review-hooks.md#injection-and-untrusted-input |
| SEC-PY-003 | Untrusted deserialization | Zero `pickle.load(s)`, `marshal.load(s)`, `shelve.open`, or `jsonpickle.decode` on data from an untrusted source; no `yaml.load` without `Loader=SafeLoader` (use `yaml.safe_load`). | S0 | knowledge/python/review-checklist/security-review-hooks.md#injection-and-untrusted-input |
| SEC-PY-004 | SQL parameterization | All SQL passed to a DB-API `execute`/`executemany` or raw ORM query uses driver placeholders or ORM binding; zero f-string, `%`, `.format`, or `+` concatenation of external input into query text. | S0 | knowledge/python/review-checklist/security-review-hooks.md#injection-and-untrusted-input |
| SEC-PY-005 | Hardcoded secrets | No string literal assigned to a name matching `(password\|passwd\|secret\|token\|api[_]?key\|access[_]?key\|private[_]?key)` and no such literal passed as a credential argument; secrets load from env or a secrets manager. | S0 | knowledge/python/review-checklist/security-review-hooks.md#secrets-and-configuration |
| SEC-PY-006 | TLS verification enabled | No `verify=False` on `requests`/`httpx` calls, no `ssl._create_unverified_context()`, and no `check_hostname=False` against non-test endpoints. | S0 | knowledge/python/review-checklist/security-review-hooks.md#transport-and-crypto |
| SEC-PY-007 | Weak hash for security | No `hashlib.md5(` / `hashlib.sha1(` used for a security purpose (matches core SEC-030); a weak digest used non-securely passes only with `usedforsecurity=False`. Password storage is SEC-PY-010 (S0). | S1 | knowledge/python/review-checklist/security-review-hooks.md#transport-and-crypto |
| SEC-PY-008 | Insecure temp files | No `tempfile.mktemp(`; temp files/dirs use `NamedTemporaryFile`, `mkstemp`, `TemporaryDirectory`, or `mkdtemp`. | S2 | knowledge/python/review-checklist/security-review-hooks.md#transport-and-crypto |
| SEC-PY-009 | assert not used for enforcement | No `assert` statement enforces a runtime validation, authorization, or security invariant in non-test code (asserts are removed under `python -O`); such checks raise a real exception. | S2 | knowledge/python/review-checklist/exceptions.md#exception-specificity |
| SEC-PY-010 | Password hashing uses a memory-hard KDF | New password stores hash with argon2id (preferred; `argon2-cffi`) or scrypt (`hashlib.scrypt` or via `cryptography`) through a vetted library; bcrypt (cost factor ≥ 12, 72-byte truncation handled) or PBKDF2 (only under a FIPS constraint recorded in the handoff, with NIST-scale iteration counts) passes only for a credential store that pre-exists the diff and whose login path rehashes verified passwords to a memory-hard KDF; zero raw digests (salted or not) or reversible encryption for stored credentials (projection of core SEC-013). | S0 | knowledge/python/review-checklist/security-review-hooks.md#transport-and-crypto |

## Quality (QUA-PY)

| ID | Check | Pass criterion | Sev | Remediation |
|---|---|---|---|---|
| QUA-PY-001 | Lint and format clean | `ruff check` reports zero violations and `ruff format --check` reports no reformatting for the changed files. | S1 | knowledge/python/uv-workflow.md#linting-and-formatting-with-ruff |
| QUA-PY-002 | Type check clean on public API | The configured checker (mypy/pyright/ty) passes with zero errors on the change, and every public (non-underscore) function/method has parameter and return annotations. | S1 | knowledge/python/review-checklist/typing.md#type-checked-public-apis |
| QUA-PY-003 | No mutable default arguments | No function/method parameter has a default that is a mutable literal or constructor (`[]`, `{}`, `set()`, `dict()`, `list()`); use `None` and build inside. Equivalent to ruff B006. | S1 | knowledge/python/review-checklist/correctness-and-pythonic-idioms.md#mutable-default-arguments |
| QUA-PY-004 | No bare or swallowed exceptions | No bare `except:`; no `except Exception` (or broad clause) whose body is only `pass`/`...` and does not log, handle, or re-raise. | S2 | knowledge/python/review-checklist/exceptions.md#exception-specificity |
| QUA-PY-005 | No wildcard imports | No `from module import *` outside a package `__init__.py` re-export. | S2 | knowledge/python/review-checklist/structure-and-maintainability.md |
| QUA-PY-006 | Network calls set timeouts | Every `requests`/`httpx` request call and raw socket connect passes an explicit `timeout=`. | S2 | knowledge/python/review-checklist/performance-and-caching.md#network-calls-and-timeouts |
| QUA-PY-007 | Coverage floor met | `pytest --cov` reports total coverage at or above the project's configured `fail_under`; the run exits zero. | S2 | knowledge/python/testing.md |

## Notes

- QUA-PY-001 subsumes pure-style rules (import order, comparisons to `None`/`True`, unused
  names); do not raise separate findings for anything ruff already reports. QUA-PY-003 and
  QUA-PY-004 are kept as named entries because they are correctness/robustness risks that
  warrant gate severity even when a project has not enabled the matching ruff rule.
- SEC-PY-002/003 refer to "untrusted source": any value crossing a trust boundary (request
  body/params, headers, file contents, env, external service). A literal or a value proven
  internal passes.
- Where a project's stack has no SQL, no async, or no outbound HTTP, the corresponding
  entries are reported as not-applicable in the verdict summary, not as passes.
