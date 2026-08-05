# Python testing with pytest

pytest is the framework; run it through uv (`uv run pytest`). The pinned major line lives in
`knowledge/shared/versions.md` — cite it rather than restating a release here. Layout: put
tests under `tests/`, share fixtures via `conftest.py`, register markers in `pyproject.toml`.
See `knowledge/quality/test-strategy.md` for the cross-language pyramid and coverage policy;
this file is the Python-specific mechanics.

## Flag cheatsheet

| Flag | Effect |
|---|---|
| `-v` / `-vv` | Verbose / show full assert diffs |
| `-q` | Quiet |
| `-s` | Do not capture stdout (show prints) |
| `--tb=short` / `long` / `line` / `no` | Traceback style |
| `-x` / `--maxfail=N` | Stop after first / N failures |
| `--lf` / `--ff` | Last-failed only / failures first |
| `--nf` | New files first |
| `--sw` | Stepwise: stop at first fail, resume there next run |
| `-k "expr"` | Select by name substring/boolean expr |
| `-m "marker"` | Select by marker (`-m "not slow"`) |
| `-ra` | Summary of all non-passing outcomes |
| `--durations=N` | Report N slowest tests |
| `-p no:cacheprovider` | Disable cache plugin |
| `--collect-only` | List tests without running |
| `--pdb` / `--trace` | Drop to pdb on failure / at test start |

## Fixtures

Fixtures provide setup/teardown and dependency injection. Scopes: `function` (default),
`class`, `module`, `package`, `session` — widen scope only for expensive, read-only setup.

```python
import pytest

@pytest.fixture
def user_repo(db_session):            # depends on another fixture
    return UserRepository(db_session)

@pytest.fixture(scope="session")
def http_client():
    client = Client()
    yield client                       # teardown after yield
    client.close()
```

- `yield` fixtures run teardown after the yield; prefer over `addfinalizer`.
- Built-ins: `tmp_path` (per-test dir), `monkeypatch` (patch attrs/env/cwd), `capsys`
  (captured stdout/stderr), `caplog` (captured log records).
- Put shared fixtures in the nearest `conftest.py`; pytest layers them by directory.
- Use `autouse=True` sparingly. Prefer explicit fixture arguments for traceability.
- Factory fixtures (fixture returns a builder function) beat many near-duplicate fixtures.

## Parametrization

```python
@pytest.mark.parametrize(
    ("value", "expected"),
    [(0, False), (1, True), (-1, True)],
    ids=["zero", "positive", "negative"],
)
def test_is_nonzero(value, expected):
    assert is_nonzero(value) is expected
```

Use `indirect=True` to route params through a fixture. Give explicit `ids` for readable
failure output.

## Mocking

Prefer real objects and dependency injection over patching. When you must fake, use
`pytest-mock`'s `mocker` fixture (auto-undo) or `unittest.mock`.

```python
def test_notify(mocker):
    sender = mocker.Mock(spec=EmailSender)      # spec rejects unknown attrs
    sender.send.return_value = True
    svc = NotifyService(sender)
    svc.notify("hi")
    sender.send.assert_called_once_with("hi")
```

- Patch where a name is *used*, not where it is defined: `mocker.patch("app.svc.Client")`.
- `return_value` for a single result; `side_effect` for sequences, exceptions, or logic.
- `spec=`/`autospec=True` keeps mocks honest against the real interface.
- Use `AsyncMock` for coroutines. Do not over-patch — deep patch chains signal a design
  that needs dependency injection instead.

## Async tests

Install `pytest-asyncio` (pinned line and its minimum-pytest pairing are in
`knowledge/shared/versions.md`). Configure the mode and default loop scope once:

```toml
[tool.pytest.ini_options]
asyncio_mode = "auto"        # every async def test runs on the loop; else use "strict"
asyncio_default_fixture_loop_scope = "function"   # 1.x: set explicitly; unset warns
```

In `strict` mode mark each with `@pytest.mark.asyncio`. Async fixtures are `async def`
with `yield`. Under the 1.x line each scope gets its own event loop: share one loop across
a module or session with `@pytest.mark.asyncio(loop_scope="module")` and a matching
`loop_scope` on async fixtures; the old `event_loop` fixture override is removed —
configure scopes instead of redefining the loop. `anyio` is an alternative for code
targeting both asyncio and trio. Use `AsyncMock` for awaited dependencies. Common failure:
"event loop is closed" from mixing `asyncio.run()` inside a test the plugin already
drives — let the plugin own the loop.

## Parallel execution

`pytest-xdist` distributes tests across workers: `uv run pytest -n auto`. Use
`--dist=loadscope` to keep a module/class on one worker when fixtures are shared.
Parallelism exposes hidden shared state and ordering assumptions — fix those rather than
serializing. Coverage under xdist needs `coverage combine` (pytest-cov handles this).

## Coverage

```bash
uv run pytest --cov=src --cov-report=term-missing --cov-branch
```

- `--cov-report=term-missing` lists uncovered line numbers; `--cov-branch` adds branch
  coverage. HTML via `--cov-report=html`.
- Enforce a floor in config (`fail_under`) and in CI. Coverage is a floor, not a target —
  high coverage with weak assertions proves little. Prioritize untested error paths and
  boundary conditions over chasing a percentage.

## Testcontainers (integration)

`testcontainers-python` runs real dependencies (PostgreSQL, Redis, Kafka, etc.) in Docker
for tests that must exercise the true backend. Requires a running Docker daemon.

```python
import pytest
from testcontainers.postgres import PostgresContainer

@pytest.fixture(scope="session")
def pg_url():
    # image tag: match the current major in knowledge/shared/versions.md
    with PostgresContainer("postgres:18") as pg:
        yield pg.get_connection_url()      # container torn down after the session
```

- Scope containers at `session` (or `module`) to avoid per-test startup cost.
- Mark integration tests (`@pytest.mark.integration`) and select with `-m` so fast unit
  tests stay separate from Docker-dependent ones.
- Keep the pyramid weighted toward fast, isolated unit tests; use Testcontainers where a
  fake would lose fidelity that matters (SQL dialect, real client behavior).

## Property-based testing

`hypothesis` generates inputs to find edge cases pure examples miss. Use `@given(...)`
with strategies for parsers, serializers, and invariants; set `@settings(deadline=...)`
for slower properties and let Hypothesis shrink failures to a minimal case.
