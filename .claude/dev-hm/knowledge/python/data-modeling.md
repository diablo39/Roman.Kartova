# Python data modeling: Pydantic v2, dataclasses, attrs

Mechanics for validated and typed data models. Which shape to choose per situation is the
decision table in `knowledge/python/typing-patterns.md#typeddict-and-choosing-a-data-shape`;
this file is the depth behind each option. Pinned library lines:
`knowledge/shared/versions.md`. Boundary rule worth repeating: validate once where data
enters (request, config, file, queue message), pass validated objects internally, and do
not re-validate trusted internal calls.

## Pydantic v2 essentials

v2 validates in Rust (`pydantic-core`); the v1 API (`class Config`, `@validator`,
`.dict()`, `.parse_obj()`) is removed — do not copy v1 snippets into new code.

```python
from pydantic import BaseModel, ConfigDict, Field

class Order(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    id: uuid.UUID
    quantity: int = Field(gt=0, le=10_000)
    unit_price: Decimal = Field(max_digits=10, decimal_places=2)
    note: str | None = None

order = Order.model_validate(payload)        # dict/attributes → validated instance
order = Order.model_validate_json(raw_bytes) # bytes/str JSON → instance (fastest path)
```

- `extra="forbid"` on inbound models rejects unexpected fields instead of silently
  dropping them — the safer default at trust boundaries.
- `frozen=True` makes instances hashable and prevents post-validation mutation.
- Constrained fields (`Field(gt=..., pattern=..., min_length=...)`) beat hand-rolled
  checks; they document themselves in the JSON schema.
- Lax vs strict: by default Pydantic coerces (`"3"` → 3). `strict=True` (per field, per
  model, or per call) disables coercion; prefer strict for machine-to-machine payloads,
  lax for human-entered config.

## Validators

```python
from pydantic import field_validator, model_validator

class Window(BaseModel):
    start: datetime
    end: datetime

    @field_validator("start", "end")
    @classmethod
    def not_naive(cls, v: datetime) -> datetime:
        if v.tzinfo is None:
            raise ValueError("timestamp must be timezone-aware")
        return v

    @model_validator(mode="after")
    def ordered(self) -> "Window":
        if self.end <= self.start:
            raise ValueError("end must be after start")
        return self
```

- `field_validator(..., mode="before")` massages raw input before type validation;
  `mode="after"` (default) sees the typed value. Cross-field rules go in a
  `model_validator(mode="after")`.
- Reusable single-field logic composes via `Annotated`:
  `NonEmptyStr = Annotated[str, AfterValidator(lambda s: s.strip() or _err())]`.
- Raise `ValueError` (Pydantic wraps it with field context); never `assert` (SEC-PY-009).
- `@computed_field` exposes a derived, serialized property; plain `@property` stays out
  of dumps.
- Discriminated unions dispatch on a `Literal` tag —
  `event: Annotated[Created | Deleted, Field(discriminator="kind")]` — giving O(1)
  validation and precise errors instead of try-each-variant.

## TypeAdapter

`TypeAdapter` validates and serializes any type without a wrapper model — the tool for
top-level lists, unions, dataclasses, or TypedDicts:

```python
ORDERS = TypeAdapter(list[Order])                 # module scope: built once
orders = ORDERS.validate_json(body)
payload = ORDERS.dump_json(orders)
```

Building an adapter (or model class) compiles a core schema — it is expensive. Create
adapters at module scope and reuse them; an adapter constructed per call can dominate the
request cost.

## Settings

`pydantic-settings` centralizes configuration: env vars, `.env` files, and secrets
directories, validated like any model.

```python
from pydantic_settings import BaseSettings, SettingsConfigDict

class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="APP_", env_nested_delimiter="__")

    database_url: PostgresDsn
    redis: RedisConfig                    # APP_REDIS__HOST=... nests
    token_secret: SecretStr               # masked in repr/logs; .get_secret_value()
```

- `SecretStr`/`SecretBytes` for every credential field — keeps secrets out of logs and
  tracebacks by construction (pairs with SEC-PY-005 and
  `knowledge/python/review-checklist/security-review-hooks.md#secrets-and-configuration`).
- Build one `Settings()` at process start and inject it; module-level singletons read at
  import time make tests and overrides painful.
- `secrets_dir` maps mounted secret files (e.g. container secret mounts) to fields.

## Serialization

```python
order.model_dump()                 # Python objects (datetime stays datetime)
order.model_dump(mode="json")      # JSON-safe primitives
order.model_dump_json(exclude_none=True, by_alias=True)
```

- Wire names differ from Python names via `Field(alias=...)` or
  `alias_generator=to_camel` in `model_config`; `populate_by_name=True` accepts both on
  input. `serialization_alias`/`validation_alias` split the two directions.
- `exclude={"internal_field"}` / `Field(exclude=True)` keep internal fields off the wire
  — an easy data-minimization win for responses.
- `@field_serializer` / `@model_serializer` customize output; prefer them over
  post-processing dumped dicts.
- `model_dump_json()` is faster than `json.dumps(model_dump(mode="json"))` — one Rust
  pass, no intermediate dict.
- Round-trip guarantee only holds for `mode="json"`-compatible types; document any field
  that does not survive dump → validate.

## Performance notes

- Validate at boundaries only; models passed internally are already trusted.
- Prefer `model_validate_json(raw)` over `model_validate(json.loads(raw))`.
- Reuse `TypeAdapter`s and model classes; never define models inside functions on hot
  paths.
- Legacy escape hatch — `model_construct()` builds instances with no validation. Use it
  only for data provably validated earlier in the same process (e.g. re-wrapping rows a
  validated query produced), never for anything that crossed a process or network
  boundary; a `model_construct` on external data is an unvalidated-input finding.

## Dataclasses

The default internal record — no validation, no dependency, real class semantics:

```python
@dataclass(slots=True, frozen=True, kw_only=True)
class Candidate:
    user_id: int
    score: float
    tags: list[str] = field(default_factory=list)
```

- `slots=True` cuts memory and catches attribute typos; `frozen=True` for value objects
  (hashable, safe to share across threads/tasks); `kw_only=True` keeps call sites
  readable and field order flexible.
- `field(default_factory=...)` for mutable defaults (QUA-PY-003 applies to dataclasses
  too); `__post_init__` for cheap derived fields — if it grows real validation, the data
  is boundary data and wants Pydantic.
- Pydantic understands stdlib dataclasses: `TypeAdapter(Candidate)` validates one without
  converting the codebase.

## attrs

attrs sits between the two: dataclass ergonomics (`@frozen`, slots by default) plus
construction-time validators and converters without pulling in Pydantic:

```python
@attrs.frozen
class Port:
    value: int = attrs.field(validator=attrs.validators.in_(range(1, 65536)))
```

Reach for it in libraries that need lightweight invariants with minimal dependencies or
maximum construction speed; in application code already using Pydantic, a second modeling
library rarely pays for itself — prefer one idiom per codebase.
