# Secure code review by vulnerability class — Exceptions and fail-closed behavior

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-070 – SEC-073 (OWASP Top 10:2025 A10 Mishandling of Exceptional Conditions). The
question for every catch block on a security path: what state does the caller see afterwards?

```python
# fail SEC-071: error path grants access
try:
    allowed = policy.check(user, resource)
except PolicyServiceError:
    allowed = True   # "temporary" availability workaround
# pass: failure denies, with distinct logging for the outage
except PolicyServiceError:
    log.error("policy check unavailable", resource=resource.id)
    raise PermissionDenied()
```

Fail-open also hides in defaults: a feature flag that defaults to permissive when the flag store
is down, a timeout that skips validation, an empty catch that lets execution continue past a
failed check (SEC-070). External error responses carry no stack traces, SQL, paths, or framework
internals — detail goes to server logs keyed by a correlation ID (SEC-072). Resources opened in
the diff are released on all exit paths via finally/using/defer/RAII (SEC-073); a return between
acquire and release is the common miss.
