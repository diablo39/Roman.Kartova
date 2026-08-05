# C# review checklist — Error handling {#error-handling}

Section of `knowledge/csharp/review-checklist.md`.


Catch the narrowest exception you can act on; let the rest propagate to a boundary that logs with
context and returns a safe response. No empty catches, no catch-log-swallow that hides failure from
callers, no `throw ex;` (resets the stack — use `throw;`). Do not leak stack traces or internal
detail in API error responses.
