# Security review hooks

These pair with the deterministic SEC-PY gate rules; see also
`knowledge/security/secure-coding-review.md`.

### Injection and untrusted input
- No `eval`/`exec`/`compile` on any externally influenced value.
- `subprocess` runs with an argument list and `shell=False`; never interpolate untrusted
  input into a `shell=True` string. Validate/allowlist any external program name.
- No deserialization of untrusted data via `pickle`, `marshal`, `shelve`, `jsonpickle`, or
  `yaml.load` without `SafeLoader`; use `yaml.safe_load` and `json`.
- SQL is parameterized (driver placeholders / ORM binding); no f-string, `%`, `.format`,
  or `+` concatenation of external input into a query passed to `execute`.

### Secrets and configuration
No credentials, API keys, or tokens as string literals in source; load from environment
or a secrets manager (`pydantic-settings` centralizes this). Keep secrets out of logs,
error messages, and default config committed to the repo.

### Transport and crypto
- TLS verification stays on — no `verify=False`, `ssl._create_unverified_context`, or
  `check_hostname=False` against real endpoints.
- No MD5/SHA-1 for security purposes; use SHA-256+ (and `usedforsecurity=False` when a
  weak hash is genuinely non-security, e.g. a cache key).
- Password hashing uses a memory-hard KDF (SEC-PY-010, S0) — never a raw digest, salted
  or not. Recommended (safe by default): argon2id via `argon2-cffi` (preferred), or
  scrypt (`hashlib.scrypt`, dependency-free). Use only if already in use: bcrypt, solely
  to verify a pre-existing bcrypt store while rehashing to argon2id/scrypt on successful
  login (bcrypt is not memory-hard and truncates input at 72 bytes). Not for new work:
  bcrypt or PBKDF2 for a new credential store.
- No predictable temp paths (`tempfile.mktemp`); use `NamedTemporaryFile`/`mkstemp`.
