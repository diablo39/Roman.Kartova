# Python dependency hygiene

Defensive controls for the Python dependency chain: what we install, how it is locked,
and how known-bad versions are caught before they ship. Cross-language supply-chain
policy (SBOM, provenance, CI posture) is `knowledge/security/supply-chain.md`; this file
is the Python/uv mechanics. Tool lines: `knowledge/shared/versions.md`.

## Lockfile integrity

`uv.lock` records the fully resolved graph with content hashes for every artifact —
commit it, and make CI treat it as authoritative:

```bash
uv sync --locked          # CI install: fails if pyproject and lockfile drifted
```

- `--locked` (or `--frozen`) is the control: without it, CI silently re-resolves and can
  pull a package version no one reviewed. Hash verification is on by default — a
  tampered or republished artifact fails installation.
- When another system must install (a platform that only reads requirements files),
  export instead of hand-maintaining a second list:
  `uv export --format requirements.txt --no-dev -o requirements.txt` — the export carries
  the same pins and hashes, so pip-based consumers get `--require-hashes` semantics.
- Review lockfile diffs like code: an update PR whose lock diff adds unexpected new
  packages or swaps an index URL deserves a closer look, not a rubber stamp.

## Vulnerability auditing

Scan the locked graph against vulnerability databases (OSV/PyPA advisories) in CI on
every lock change plus on a schedule (new advisories arrive without any diff):

```bash
uv audit                       # uv-native scan of the project's resolved deps (preview)
uvx pip-audit                  # standalone auditor; also fixes via --fix in simple cases
```

`uv audit` status and line are anchored in `knowledge/shared/versions.md`; while it is
preview, treat pip-audit as the stable second opinion. Triage findings by reachability
and fix availability — upgrade first (`uv lock --upgrade-package name`), pin a patched
minimum in `pyproject.toml` when the resolver keeps selecting a vulnerable one, and
record an explicit accepted-risk note (id, reason, expiry) for anything unfixable now;
a silent ignore list is how known vulnerabilities become permanent. uv can also run an
OSV-backed malware lookup on sync (opt-in; see versions.md) — enable it in CI once out
of preview.

## Adding a dependency deliberately

Every `uv add` is an act of trust in code that runs inside the process (and whose build
hooks may run at install time). Before adding:

- Verify the exact name on PyPI — typosquats rely on hurried installs (`request` vs
  `requests`, hyphen/underscore swaps). Copy the name from the project's own docs or
  repo, never from an error message or a model suggestion, and check the PyPI page
  points at the repository you think it does (trusted-publisher/provenance markers on
  PyPI strengthen that link).
- Prefer the maintained, widely-used option; a dependency saves writing code but adds a
  maintenance surface forever. For trivial needs, the stdlib or twenty lines of your own
  code beats a new trust relationship.
- Check pulse: recent releases, issue responsiveness, more than one maintainer for
  anything load-bearing. An abandoned package is a future forced migration.
- Scope it: dev-only tools go in `[dependency-groups] dev`, never runtime dependencies;
  runtime images install `--no-dev` (`knowledge/python/packaging.md#container-packaging`).

## Index hygiene (dependency confusion)

If the project uses a private index, configure it explicitly and pin internal packages to
it — the classic confusion attack publishes a higher version of your internal name on
PyPI and wins a naive resolution:

```toml
[[tool.uv.index]]
name = "internal"
url = "https://pypi.internal.example.com/simple"
explicit = true                        # only used when a package names it

[tool.uv.sources]
corp-billing = { index = "internal" }  # internal names can never resolve from PyPI
```

- `explicit = true` plus a `tool.uv.sources` entry per internal package is the safe
  default; a flat "extra index" that merges public and private candidates is the
  vulnerable shape.
- Never embed credentials in index URLs in committed config; use env-based credentials
  (`UV_INDEX_<NAME>_USERNAME`/`_PASSWORD` or keyring) per
  `knowledge/security/secrets-and-keys.md`.

## Updating

- Update deliberately and often: small, reviewed `uv lock --upgrade-package` bumps beat
  rare wholesale `uv lock --upgrade` events — smaller diffs, easier bisection when
  something regresses.
- Automate the proposal side (Renovate/Dependabot produce the PR), keep the merge human:
  CI runs the full gate plus audit on the new lock; a maintainer reads the changelog for
  majors.
- Don't chase day-zero releases of deep dependencies without cause; a short soak time is
  cheap insurance against both regressions and compromised-release incidents, while
  security fixes go in immediately.
- Keep the tree small: `uv tree` shows what drags what in; prune extras you do not use
  (`package[extra]` pulls real dependencies).

## CI wiring

Add two steps to the standard gate from
`knowledge/python/uv-workflow.md#ci-wiring-one-gate-per-concern`:

```bash
uv sync --locked               # integrity: env == reviewed lockfile
uv audit                       # advisories: fail on known-vulnerable resolution
```

plus a scheduled (e.g. daily) audit-only job on the default branch, so advisories
published after the last merge still page someone. Findings route through the security
gate — the severity mapping and waiver flow are the SEC supply-chain entries in
`oracles/security-oracle.md`, adjudicated per `knowledge/shared/defense-in-depth.md`.
