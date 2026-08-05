# Typing

### Type-checked public APIs
Every public (non-underscore) function, method, and returned value carries type
annotations, and the configured checker (mypy / pyright / pyrefly / ty) passes with zero
errors on the change. Prefer `X | None` unions (3.10+), `collections.abc` protocols, and
`typing.Protocol` for structural typing over concrete imports. Treat `Any` as a smell;
justify each `# type: ignore[code]` with its error code. Model structured data with
dataclasses, `TypedDict`, or Pydantic rather than untyped dicts. Severity S1 for missing
annotations on public API or a failing type check (gate QUA-PY). Depth — generics,
Protocol, overloads, narrowing, and the data-shape decision table — in
`knowledge/python/typing-patterns.md`.
