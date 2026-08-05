# Python web service patterns (FastAPI)

FastAPI-specific patterns: lifespan-managed resources, dependency injection, request and
response modeling, and in-process testing. FastAPI builds on Starlette (ASGI) and
Pydantic v2 — model depth is `knowledge/python/data-modeling.md`, async correctness
`knowledge/python/async-patterns.md`. Framework lines: `knowledge/shared/versions.md`.

## Lifespan: process-scoped resources

Create shared clients and pools once per process in a lifespan context manager — not at
import time (breaks tests and tooling) and not per request (kills connection reuse). The
older `@app.on_event("startup"/"shutdown")` hooks are deprecated; migrate them to
lifespan when touched.

```python
from contextlib import asynccontextmanager

@asynccontextmanager
async def lifespan(app: FastAPI):
    async with httpx.AsyncClient(timeout=10.0) as http, make_db_pool() as pool:
        yield {"http": http, "db": pool}      # available as request.state.http / .db
    # teardown ran via the async withs — also on shutdown signals

app = FastAPI(lifespan=lifespan)
```

## Dependency injection

`Depends` is the seam for testability — handlers declare what they need, tests override it.

```python
Settings_ = Annotated[Settings, Depends(get_settings)]           # cached per process
DB = Annotated[AsyncConnection, Depends(get_conn)]

async def get_conn(request: Request) -> AsyncIterator[AsyncConnection]:
    async with request.state.db.acquire() as conn:               # yield-dependency:
        yield conn                                                # cleanup after response

@app.get("/users/{user_id}")
async def get_user(user_id: int, db: DB, settings: Settings_) -> UserOut: ...
```

- Declare dependencies with `Annotated[T, Depends(fn)]` type aliases; reuse them across
  handlers.
- Yield-dependencies own per-request resources (transactions, sessions); code after
  `yield` runs after the response, including on handler exceptions.
- A dependency used by every route (auth!) attaches at router level:
  `APIRouter(dependencies=[Depends(require_user)])` — per-handler opt-in auth is how
  endpoints ship unauthenticated (core SEC-010/011 apply).
- Within one request, the same dependency resolves once and is shared; process-wide
  singletons come from lifespan state, not module globals.

## Sync and async handlers

- `async def` handlers run on the event loop: nothing blocking inside — blocking I/O or
  CPU work goes through `asyncio.to_thread` / a pool
  (`knowledge/python/async-patterns.md#bridging-sync-and-async`).
- `def` (sync) handlers are valid: Starlette runs them in a bounded threadpool. A mostly
  sync stack (blocking ORM/SDK) is better served by honest `def` handlers than by
  `async def` ones that block the loop — that one mistake serializes the whole service.
- Do not mix per endpoint arbitrarily; decide per service which dependencies are async
  and keep handlers consistent with the I/O stack they use.

## Requests, responses, errors

- Pydantic models validate the request body; constrained query/path params via
  `Annotated[int, Query(ge=1, le=500)]`. Set `extra="forbid"` on inbound models.
- Declare the return type (or `response_model=`): it filters the response to the declared
  fields — the mechanism that keeps internal fields off the wire; pair with
  field-level `exclude` (`knowledge/python/data-modeling.md#serialization`).
- Raise `HTTPException(status_code, detail)` for expected failures; map domain errors
  once with `@app.exception_handler(DomainError)` instead of try/except per handler.
  Never let raw exception text or stack traces reach a response — log the detail
  server-side with a correlation id and return a generic message (core SEC error-handling
  entries apply).
- `StreamingResponse` for large or incremental payloads; `BackgroundTasks` only for
  fire-and-forget work that may be lost on crash or deploy — anything requiring delivery
  goes to a real queue/outbox, not an in-process callback.

## Testing through ASGITransport

Test the app in-process — routing, validation, middleware, DI included — without a
socket, by pointing httpx at the ASGI app:

```python
from asgi_lifespan import LifespanManager   # ASGITransport does not run lifespan itself

@pytest.fixture
async def client():
    app.dependency_overrides[get_conn] = fake_conn          # swap real deps
    async with LifespanManager(app) as mgr:
        transport = httpx.ASGITransport(app=mgr.app)
        async with httpx.AsyncClient(transport=transport, base_url="http://test") as c:
            yield c
    app.dependency_overrides.clear()

async def test_get_user(client):
    resp = await client.get("/users/42")
    assert resp.status_code == 200
    assert UserOut.model_validate(resp.json()).id == 42
```

- `dependency_overrides` replaces real dependencies (DB, auth, clocks) per test; always
  clear it in teardown.
- For integration tests, override only infrastructure wiring and hit a Testcontainers
  backend (`knowledge/python/testing.md#testcontainers-integration`).
- The sync `TestClient` (Starlette wrapping the same transport) is fine for a sync test
  suite; async fixtures above fit pytest-asyncio setups
  (`knowledge/python/testing.md#async-tests`).
- Assert responses by validating them with the response model (as above) rather than
  spot-checking dict keys — schema drift then fails loudly.

## Serving

- Run under uvicorn; scale with multiple worker processes (`--workers N` or a process
  manager) — workers share nothing, so lifespan state is per worker and anything shared
  lives in external stores.
- Behind a reverse proxy, enable `--proxy-headers` (and set `--forwarded-allow-ips`) so
  scheme/client IP are correct; otherwise auth redirects and logs lie.
- Graceful shutdown: uvicorn stops accepting, waits for in-flight requests, then runs
  lifespan teardown — keep teardown fast and idempotent; readiness probes should flip
  before shutdown starts in orchestrated deployments.
- Set explicit timeouts end to end: server keep-alive, upstream client timeouts
  (QUA-PY-006), and the proxy's — the missing one becomes the incident.
