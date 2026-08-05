# Python packaging: layout, building, publishing, containers

How dev-hm Python projects are structured, built into wheels, published, and shipped in
containers — uv-native throughout (project workflow basics:
`knowledge/python/uv-workflow.md`; tool lines: `knowledge/shared/versions.md`).
Dependency vetting and lockfile integrity are `knowledge/python/deps-hygiene.md`.

## src layout

```
project/
├── pyproject.toml
├── uv.lock
├── src/
│   └── my_pkg/
│       ├── __init__.py
│       └── py.typed          # ship type info (see below)
└── tests/                    # outside the package: not shipped, imports the installed pkg
```

Use `src/` for anything built into a wheel (`uv init --lib` scaffolds it). The point:
tests and tools import the installed package, not the working directory — a missing file
in the wheel fails in CI instead of at a user's site. Flat layout is acceptable only for
non-packaged applications that are deployed as a synced environment, not built.

## Metadata that matters

```toml
[project]
name = "my-pkg"
version = "1.4.0"
requires-python = ">=3.X"            # floor = oldest line you test; current supported
                                     # lines are in knowledge/shared/versions.md
dependencies = ["httpx>=X.Y", "pydantic>=X.Y"]   # floors you actually require

[project.optional-dependencies]      # user-facing extras: my-pkg[cli]
cli = ["click"]

[dependency-groups]                  # dev-only, never published
dev = ["pytest", "ruff"]
```

- Library dependency bounds: set floors you actually require; avoid speculative upper
  caps (`<2`) that force resolution conflicts downstream — cap only on known
  incompatibility. Applications pin exactly via `uv.lock` instead.
- Extras are for users (`[cli]`, `[postgres]`); dependency groups are for development.
- Bump versions with `uv version --bump patch|minor|major` rather than hand-editing.

## Build backends

Declared under `[build-system]`; uv scaffolds the default for you.

| Backend | Use when | Note |
|---|---|---|
| `uv_build` | Pure-Python packages — the default for new work | Stable, zero-config for the standard src layout, fastest; status per `knowledge/shared/versions.md` |
| `hatchling` | You need build plugins/hooks (dynamic versioning from VCS, asset pipelines) | Mainstream, well documented |
| `maturin` | Rust extension modules | PyO3 standard |
| `scikit-build-core` / `meson-python` | C/C++/Fortran extensions via CMake / Meson | Successors to hand-rolled setup.py builds |

Legacy — `setuptools` only for packages that already carry a working `setup.py`/
`setup.cfg` build (typically with custom commands) where migration is not yet budgeted;
migrate pure-Python ones to `uv_build` when touched, extension builds to the matching
backend above. Do not start a new package on setuptools.

## Entry points

```toml
[project.scripts]
my-tool = "my_pkg.cli:main"                  # console command → my_pkg/cli.py:main()

[project.entry-points."my_pkg.plugins"]      # plugin discovery group
s3 = "my_pkg_s3:S3Backend"
```

- `project.scripts` is the only supported way to ship a CLI — never instruct users to run
  a module file directly.
- Named entry-point groups implement plugin architectures: the host iterates
  `importlib.metadata.entry_points(group="my_pkg.plugins")` and loads what it finds.
  Loading an entry point executes third-party code — load only from environments you
  control, and document that installing a plugin is granting it execution.

## Building and publishing

```bash
uv build                        # dist/*.whl + dist/*.tar.gz (sdist)
uv build --package my-pkg       # one member of a workspace
uv publish                      # upload; use trusted publishing in CI
```

- Verify the artifact, not just the tree: install the built wheel into a scratch env and
  import/run it (`uv run --isolated --with dist/my_pkg-*.whl --no-project -- python -c
  "import my_pkg"`), or run the test suite against it in CI.
- Check the sdist contains everything the wheel build needs (license, `py.typed`, data
  files) — sdist consumers rebuild from it.
- Publishing from CI: use trusted publishing (OIDC) — the registry trusts the specific
  CI workflow identity and issues short-lived tokens, so no long-lived credential exists
  to leak. Legacy — a project-scoped API token stored as a CI secret only where the CI
  platform or registry does not support OIDC; rotate it and scope it to the one project.
- Private registries: configure `[[tool.uv.index]]` with a name and URL; pair with the
  index-hygiene rules in `knowledge/python/deps-hygiene.md` so internal names cannot be
  shadowed from public indexes.

## Shipping type information

A typed package must include an empty `py.typed` marker file inside the package, or
downstream type checkers ignore every annotation you wrote. Backends ship it
automatically once it exists in `src/my_pkg/`. Stub-only distributions (`my-pkg-stubs`)
are for typing third-party code you do not control.

## Container packaging

Multi-stage: build the environment with uv, copy only the result; no uv, no lock
tooling, no dev deps in the final image.

```dockerfile
FROM ghcr.io/astral-sh/uv:python3.14-bookworm-slim AS builder
ENV UV_COMPILE_BYTECODE=1 UV_LINK_MODE=copy
WORKDIR /app
# Layer-cache deps: lockfile first, project source second
RUN --mount=type=cache,target=/root/.cache/uv \
    --mount=type=bind,source=uv.lock,target=uv.lock \
    --mount=type=bind,source=pyproject.toml,target=pyproject.toml \
    uv sync --locked --no-install-project --no-dev
COPY . /app
RUN --mount=type=cache,target=/root/.cache/uv uv sync --locked --no-dev

FROM python:3.14-slim-bookworm
RUN groupadd -r app && useradd -r -g app app
COPY --from=builder --chown=app:app /app /app
ENV PATH="/app/.venv/bin:$PATH"
USER app
CMD ["python", "-m", "my_pkg"]
```

(Image tags track the interpreter line in `knowledge/shared/versions.md`; update them
together.)

- Two-step sync (`--no-install-project`, then full) keeps the dependency layer cached
  across source-only changes — the difference between 2-second and 2-minute rebuilds.
- `--locked` makes a drifted lockfile fail the build; `--no-dev` keeps test/lint tooling
  out of the runtime image; `UV_COMPILE_BYTECODE=1` trades build time for startup time.
- Run as a non-root user; the venv is self-contained, so the final stage needs no uv and
  no compiler — smaller image, smaller attack surface.
- For applications, prefer this synced-environment shape over building a wheel and
  installing it; build wheels when the deliverable is a library or a versioned artifact
  consumed elsewhere.
