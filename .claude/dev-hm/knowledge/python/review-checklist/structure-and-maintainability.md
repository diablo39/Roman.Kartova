# Structure and maintainability

Favor dependency injection and small single-purpose functions; avoid global mutable state
and import-time side effects. Follow PEP 8 naming and PEP 257 docstrings on public API
(style is enforced by ruff, not prose review). No wildcard imports (`from x import *`)
outside a package `__init__` re-export — they obscure origins and defeat static analysis.
Model data with dataclasses/Pydantic instead of passing around raw dicts.
