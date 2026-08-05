# Python typing patterns

Depth reference behind gate QUA-PY-002 (type check clean, annotated public API) and
`knowledge/python/review-checklist/typing.md#type-checked-public-apis`. Checker selection and CI
wiring are in `knowledge/python/uv-workflow.md#type-checking`; interpreter feature floors
below are language facts (the version that introduced the syntax), not toolchain pins —
current supported lines are in `knowledge/shared/versions.md`.

## Generics: PEP 695 syntax (3.12+)

Declare type parameters inline; variance is inferred, and no `TypeVar` boilerplate:

```python
class Repository[T]:                      # instead of Generic[T] + TypeVar
    def get(self, key: str) -> T | None: ...
    def put(self, key: str, value: T) -> None: ...

def first[T](items: Sequence[T], default: T) -> T: ...

def parse[T: (int, float)](raw: str, target: type[T]) -> T: ...   # constrained
class Cache[K: Hashable, V]: ...                                   # bounded

type JSON = dict[str, "JSON"] | list["JSON"] | str | int | float | bool | None
type Pair[T] = tuple[T, T]                # generic alias via the type statement
```

Use the `type` statement for aliases — it is lazily evaluated (forward references just
work) and checkers treat it as an alias, not a variable. Keep the old
`TypeVar`/`Generic` spelling only in code that must run on pre-3.12 interpreters.

## Protocols: structural typing

`typing.Protocol` types the capability, not the class hierarchy — the right tool for
dependency injection seams, because implementations need not import or subclass anything:

```python
class Clock(Protocol):
    def now(self) -> datetime: ...

class UserService:
    def __init__(self, clock: Clock, repo: UserRepo) -> None: ...
```

- Accept protocols / `collections.abc` types in parameters; return concrete types.
- `@runtime_checkable` allows `isinstance`, but it checks only that the methods exist —
  not their signatures. Do not use it as validation of untrusted objects.
- Prefer a Protocol over an ABC when implementors are outside your control or the
  interface is small; keep ABCs where you want shared behavior and registration.
- A protocol with attributes (`name: str`) works for typed duck-typed records too.

## Overloads

`@overload` documents input-dependent return types the implementation cannot express:

```python
@overload
def get_config(key: str) -> str | None: ...
@overload
def get_config(key: str, default: str) -> str: ...
def get_config(key: str, default: str | None = None) -> str | None:
    return _store.get(key, default)
```

Keep overload sets small and non-overlapping; if you need many, the function wants to be
several functions. `typing.assert_type(expr, T)` pins expected inference in tests.

## Self and ParamSpec

- `Self` (3.11+) types fluent APIs and alternate constructors so subclasses keep their own
  type: `def with_timeout(self, t: float) -> Self: ...`,
  `@classmethod def from_env(cls) -> Self: ...`.
- `ParamSpec` types decorators that preserve signatures; with PEP 695 it is declared
  inline:

```python
def retried[**P, R](fn: Callable[P, R]) -> Callable[P, R]:
    @functools.wraps(fn)
    def wrapper(*args: P.args, **kwargs: P.kwargs) -> R:
        ...
    return wrapper
```

`Concatenate[Arg, P]` handles decorators that add or consume a leading argument. A
decorator typed `Callable[..., Any] -> Callable[..., Any]` erases the API it wraps —
treat that as a finding on public decorators.

## TypedDict, and choosing a data shape

`TypedDict` types dict-shaped data you do not control (JSON payloads, kwargs, rows):

```python
class UserRow(TypedDict):
    id: int
    email: str
    nickname: NotRequired[str]            # per-key optionality
    created: ReadOnly[datetime]           # 3.13+: checker-enforced immutability

def create_user(**kwargs: Unpack[UserRow]) -> User: ...   # typed **kwargs
```

A TypedDict is erased at runtime — no validation, no defaults, still a plain dict.

| Need | Reach for | Why |
|---|---|---|
| Validate data crossing a trust boundary (request, config, file, queue) | Pydantic model | Runtime validation + coercion + serialization; see `knowledge/python/data-modeling.md` |
| Internal typed record passed between your own functions | `@dataclass(slots=True)` (add `frozen=True` for value objects) | Cheap, real class, no validation overhead where inputs are already trusted |
| Type an existing dict shape without changing runtime behavior | `TypedDict` | Zero-cost annotation of dict-in/dict-out code and `**kwargs` |
| Dataclass ergonomics plus field validators, without Pydantic | attrs | Mature, fast, validators/converters at construction |
| Fixed small heterogeneous group, positional | `NamedTuple` | Indexable, immutable, lightweight |

Default rule: validate once at the boundary with Pydantic, then pass dataclasses (or the
validated models themselves) internally — do not re-validate trusted internal calls, and
do not let raw dicts travel more than one call deep. Depth on each option:
`knowledge/python/data-modeling.md`.

## Narrowing

Checkers narrow on `isinstance`, `is None` checks, `match`, walrus-captured checks, and
returns/raises. Make narrowing explicit at trust boundaries:

- `TypeIs[T]` (3.13+) for user-defined narrowing functions — it narrows in both branches
  and behaves like `isinstance`. Prefer it over `TypeGuard`, which narrows only the
  positive branch; keep `TypeGuard` where the checked type is not a subtype of the input
  (e.g. `list[object]` → `list[str]`).

```python
def is_ok[T](resp: Result[T] | Error) -> TypeIs[Result[T]]:
    return resp.code == 0
```

- Exhaustiveness: end `match`/`if-elif` chains over unions and enums with
  `assert_never(value)` so adding a variant fails the type check instead of falling
  through silently.
- Discriminated unions: give each variant a `Literal` tag field and switch on it — the
  checker narrows per branch; Pydantic uses the same tag for validation dispatch.
- Avoid `cast` where a narrowing function or an assertion (`assert isinstance(...)` in
  non-enforcement positions) can prove the type; each `cast` is an unchecked promise.

## Annotation hygiene

- `Annotated[T, meta]` attaches metadata without changing the type — the standard channel
  for Pydantic `Field`/validators and FastAPI dependency declarations.
- `@override` (3.12+) on every intentional method override; the checker then catches
  renamed-base-method drift.
- `Final` for module constants; `ClassVar` to keep class-level state out of instance
  fields (dataclasses require it).
- `NewType("UserId", int)` distinguishes ids and other same-representation values at
  check time for free at runtime.
- Deferred annotations: on the current line (3.14+) annotations evaluate lazily by
  default (PEP 649/749), so forward references work without quotes and without
  `from __future__ import annotations`. Keep the future-import only in code that must run
  on older interpreters; do not add it to new current-line code.
- `Any` disables checking transitively — prefer `object` for "anything, but prove it
  before use". Every `# type: ignore[code]` carries its error code and ideally a reason;
  a bare ignore is a finding (`knowledge/python/review-checklist/typing.md`).
- Ship `py.typed` in packages so downstream checkers see your annotations
  (`knowledge/python/packaging.md`).
