# Python logging and observability

Python-specific mechanics for logs, context propagation, and OpenTelemetry wiring. The
cross-language observability strategy (what to measure, SLOs, dashboards) is
`knowledge/quality/observability.md`; which security events must be logged and kept
secret-free is `knowledge/security/security-logging-detection.md`. Signal and SDK status:
`knowledge/shared/versions.md`.

## stdlib logging: the rules that prevent pain

```python
log = logging.getLogger(__name__)          # per-module logger, named by module path
```

- Configure once, at the process entry point (`dictConfig`); libraries never configure —
  no `basicConfig`, no handlers, no level-setting in importable code. A library that
  wants silence-by-default adds a `NullHandler` to its top-level logger.
- Log with lazy `%s` arguments — `log.info("user %s created", user_id)` — not f-strings:
  formatting cost is skipped when the level is off, and message templates stay constant
  for aggregation and dedup.
- `log.exception("context")` inside an `except` block captures the traceback
  (`exc_info=True` elsewhere). Log-and-swallow still counts as swallowed for QUA-PY-004
  unless the failure is genuinely handled.
- Levels: `logging.getLogger("noisy.dependency").setLevel(logging.WARNING)` tames chatty
  third-party loggers from config, not by editing their code.
- Uncaught crash paths: `sys.excepthook` (threads: `threading.excepthook`; asyncio:
  `loop.set_exception_handler`) so the last exception is logged, not printed;
  `logging.captureWarnings(True)` routes `warnings` through logging.

## Structured logs

Emit key-value/JSON logs; grep-era prose does not survive aggregation. Two workable
shapes:

- structlog (the common choice): `log = structlog.get_logger()`;
  `log.info("order_created", order_id=oid, total=total)`; `bind()` attaches context to a
  logger instance; its stdlib integration formats third-party records through the same
  JSON renderer, so one pipeline serves both.
- stdlib-only: pass `extra={"order_id": oid}` and install a JSON formatter; workable when
  a dependency budget forbids structlog, but discipline is on you.

Rules either way: event names are stable snake_case identifiers with data in fields, not
interpolated prose (the OWASP logging vocabulary in `knowledge/shared/versions.md` names
security events); no secrets, tokens, or raw PII in any field — `SecretStr` fields from
`knowledge/python/data-modeling.md#settings` mask themselves, and dumps of whole request
objects are how tokens leak into logs.

## Request/task context via contextvars

`contextvars.ContextVar` is the Python mechanism for request-scoped log context: it
propagates across `await`, into tasks (each task copies the context at creation), and
into `asyncio.to_thread`.

```python
request_id: ContextVar[str] = ContextVar("request_id", default="-")

# middleware / consumer entry:
request_id.set(rid)

# logging side: a filter (or structlog contextvars processor) injects it per record
class ContextFilter(logging.Filter):
    def filter(self, record):
        record.request_id = request_id.get()
        return True
```

structlog users get this prebuilt (`structlog.contextvars.bind_contextvars`). Raw
executor calls and hand-spawned threads do not inherit context — copy it explicitly
(`knowledge/python/async-patterns.md#bridging-sync-and-async`).

## Non-blocking logging in servers

Handlers that touch disk or network block the caller — in an async service that stalls
the event loop. Route records through a queue: `QueueHandler` on the loggers,
`QueueListener` writing on its own thread (`dictConfig` can wire the pair directly on
current lines via the `queue_handler`/`respect_handler_level` schema). Rotation on the
listener side (`RotatingFileHandler`/`TimedRotatingFileHandler`) — or, in containers, log
to stdout and let the platform collect, which is the default posture for
`knowledge/python/packaging.md#container-packaging` deployments.

## OpenTelemetry

OTel is the vendor-neutral standard (signal status per `knowledge/shared/versions.md`).
Python wiring, least-effort first:

- Zero-code: `opentelemetry-instrument python -m my_app` (with
  `opentelemetry-distro` + exporter packages) auto-instruments supported libraries —
  FastAPI/ASGI, httpx, DB drivers — via env-var configuration (`OTEL_SERVICE_NAME`,
  `OTEL_EXPORTER_OTLP_ENDPOINT`).
- Library instrumentors in code when you need control:
  `FastAPIInstrumentor.instrument_app(app)`, `HTTPXClientInstrumentor().instrument()`.
- Manual spans only around meaningful units of work:

```python
tracer = trace.get_tracer(__name__)
with tracer.start_as_current_span("reprice_basket", attributes={"basket.size": n}):
    ...
```

- Correlate logs with traces: the logging instrumentation stamps `trace_id`/`span_id`
  onto records (structlog: add a processor reading the current span) — that join is most
  of tracing's debugging value.
- Metrics: counters and histograms via `metrics.get_meter(__name__)`; measure at
  boundaries (requests, queue consumes, external calls), and let the SDK aggregate —
  do not hand-roll percentiles.
- Exporters/endpoint come from deployment env vars, not code, so the same image ships to
  every environment.

## What to instrument (Python service checklist)

- One log line per unit of work (request, message, job) with outcome, duration, and ids —
  not one per step; DEBUG carries the steps.
- Every external call wrapped in a span (auto-instrumentation covers the usual clients);
  every queue consume continues the producer's trace context where the transport allows.
- Security-relevant events (authn/authz failures, validation rejects at trust boundaries)
  logged per `knowledge/security/security-logging-detection.md` — these are gate-checked,
  not optional niceties.
- Health/readiness endpoints excluded from traces and request logs, or they drown the
  signal.
- A crashing worker logs why: exception hooks above plus `faulthandler` enabled in
  production entry points (`knowledge/python/debugging.md#crashes-and-stack-traces`).
