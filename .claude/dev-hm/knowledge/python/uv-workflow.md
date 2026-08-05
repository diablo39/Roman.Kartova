# Python toolchain: uv, ruff, type checking

The dev-hm Python stack is uv-native. uv manages interpreters, virtual environments,
dependencies, lockfiles, and tool execution; ruff lints and formats; a type checker
gates public APIs; pytest runs tests. This supersedes older pip + venv + pyenv + pipx +
black + isort + flake8 workflows — do not mix `pip install` into a uv-managed project.

Pinned versions live in `knowledge/shared/versions.md` — cite it rather than restating a
current release here. Baseline practice: support the current stable CPython lines recorded
there and declare `requires-python` in `pyproject.toml`. The current stable line recorded
there ships an officially supported (no longer experimental) free-threaded build under
PEP 779, opt-in as a separate `t`-suffixed interpreter. Enable it only when a workload is
measured to benefit and its dependencies support the ABI: the default build keeps the GIL,
and free-threading still carries a small single-thread overhead and exposes latent races.

## uv project model

A uv project is defined by `pyproject.toml` (metadata, dependencies, tool config) plus a
committed `uv.lock` (fully resolved, cross-platform, hashed). The environment (`.venv/`)
and lockfile are created lazily on the first `uv sync`, `uv add`, or `uv run`.

| Task | Command |
|---|---|
| Scaffold a project | `uv init` (app) / `uv init --lib` (library) |
| Add a runtime dep | `uv add httpx` |
| Add a dev/test dep | `uv add --dev pytest ruff` (goes to `[dependency-groups] dev`) |
| Add an optional extra | `uv add --optional cli click` |
| Remove a dep | `uv remove httpx` |
| Resolve + write lockfile | `uv lock` (`--upgrade` / `--upgrade-package httpx` to bump) |
| Sync env to lockfile | `uv sync` (`--frozen` skips re-lock, `--locked` fails if stale) |
| Run in the project env | `uv run pytest` / `uv run python -m app` |
| Run a tool ephemerally | `uvx ruff check` (isolated, no project install) |
| Install a global tool | `uv tool install ruff` |
| Install an interpreter | `uv python install <X.Y>` (current stable line: `knowledge/shared/versions.md`) |
| Pin the interpreter | `uv python pin <X.Y>` (writes `.python-version`) |
| Build wheel/sdist | `uv build` |

Prefer `uv run <cmd>` over activating the venv manually — it guarantees the command runs
against the synced, locked environment. Dependency version overrides, constraints, and
alternate resolution strategies are configured under `[tool.uv]` in `pyproject.toml`.

## Workspaces

For a multi-package repository, uv supports Cargo-style workspaces: each member keeps its
own `pyproject.toml`, but the workspace shares one root `uv.lock`, so every member resolves
against a single consistent set of versions. Declare members with `members` / `exclude`
globs under `[tool.uv.workspace]`. `uv lock` resolves the whole workspace at once; `uv run`
and `uv sync` act on the workspace root by default, or on a chosen member with
`--package <name>`. Depend on a sibling by marking its `[tool.uv.sources]` entry
`{ workspace = true }`. Use a workspace for tightly coupled packages released together;
prefer separate projects (path or index sources) when members version independently.

## Reproducible environments

- Commit `uv.lock`. It is the source of truth for exact versions and hashes.
- In CI, run `uv sync --locked` (or `--frozen`) so a drifted lockfile fails the build
  instead of silently re-resolving.
- `requires-python` in `pyproject.toml` bounds the supported interpreter range; keep it
  consistent with `.python-version`.
- For containers, `uv sync --no-dev --locked` yields a lean runtime environment; use
  `--compile-bytecode` to precompile on install.

## Linting and formatting with ruff

Ruff is a single Rust tool replacing flake8, isort, pyupgrade, and black. It ships 900+
rules and a black-compatible formatter (pinned line and style-guide edition:
`knowledge/shared/versions.md`).

```toml
# pyproject.toml
[tool.ruff]
target-version = "py313"      # example — match your requires-python floor; current
                              # interpreter lines are in knowledge/shared/versions.md
line-length = 100

[tool.ruff.lint]
select = ["E", "F", "I", "B", "UP", "SIM", "PL", "RUF"]  # style, pyflakes, imports,
                                                          # bugbear, pyupgrade, simplify
```

| Task | Command |
|---|---|
| Lint | `uv run ruff check` |
| Lint + autofix | `uv run ruff check --fix` |
| Format (write) | `uv run ruff format` |
| Format check (CI) | `uv run ruff format --check` |

The `B` (flake8-bugbear) set catches real bug risks such as mutable default arguments
(B006) and is worth enabling. Enable `pytest` (`PT`) and `async` (`ASYNC`) rule groups
for test- and async-heavy code.

## Type checking

Static typing is a quality gate, not optional decoration. Pick one checker, configure it
strict on new/public code, and run it in CI.

| Checker | Notes |
|---|---|
| mypy | Most mature; the plugin ecosystem (Django, SQLAlchemy) keeps it the safe default for those stacks, though its typing-spec conformance now trails the Rust checkers. |
| pyright | Fast, strict, powers Pylance in VS Code; excellent inference and narrowing; the highest typing-spec conformance of the widely-used checkers. |
| pyrefly (Meta) | Rust, very fast; reached a stable 1.0 release, is Meta's default (Instagram) and runs on PyTorch/NumPy. Strong choice for new projects; near-pyright spec conformance and an auto-migration path from mypy. |
| ty (Astral) | Rust, 10–60x faster; still in beta with a 1.0 targeted but undated. Excellent editor/CI speed on new code; lower spec conformance today and not a drop-in mypy replacement where mypy plugins are required. |

Guidance: pick one checker and gate it in CI. mypy and pyright remain safe defaults; pyrefly
is a fast, now-stable alternative well suited to new projects; adopt ty where its speed helps
and no mypy-plugin dependency blocks it. Run under `uv run mypy src` / `uv run pyright` /
`uv run pyrefly check` / `uv run ty check`.
Configure via `[tool.mypy]` / `pyrightconfig.json` / `[tool.ty]`. Enable strict mode and
disallow untyped defs for public modules; annotate all public function signatures. See
`knowledge/python/review-checklist/typing.md#type-checked-public-apis`.

## Data and web stacks

Pydantic v2 (validation core `pydantic-core` in Rust) is the standard for validated data
models and settings (`pydantic-settings`); it is 3–50x faster than v1 and v1 is retired.
FastAPI builds on Pydantic v2 for request/response models. Use `model_config`,
`Field(...)`, and validators; avoid the removed v1 `Config` class and `@validator` in new
code (use `@field_validator` / `@model_validator`). Depth: `knowledge/python/data-modeling.md`
(validators, TypeAdapter, settings) and `knowledge/python/web-service-patterns.md` (FastAPI
lifespan, DI, testing). Building and shipping the project itself:
`knowledge/python/packaging.md`; dependency vetting: `knowledge/python/deps-hygiene.md`.

## CI wiring (one gate per concern)

```bash
uv sync --locked
uv run ruff check
uv run ruff format --check
uv run <type-checker>          # mypy / pyright / pyrefly / ty
uv run pytest --cov=src --cov-report=term-missing
```

Each step is deterministic and maps to an oracle entry in `oracles/addenda/python.md`
(ruff clean, type-check clean, coverage floor). Keep lint/format/type/test as separate CI
steps so a failure names the concern precisely.
