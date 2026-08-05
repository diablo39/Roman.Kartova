# Exceptions

### Exception specificity
Catch the narrowest exception that applies; never a bare `except:` (also catches
`KeyboardInterrupt`/`SystemExit`) and never `except Exception: pass` that silently
swallows failures. Chain with `raise NewError(...) from err` to preserve the cause. Do not
use exceptions for ordinary control flow in hot paths. Do not use `assert` to enforce
runtime validation or security checks — asserts are stripped under `python -O`; raise a
real exception instead. Severity S2 for bare/swallowed except; S1 when it hides a failure
that affects correctness or security. See gate QUA-PY and SEC-PY.
