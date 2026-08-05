# Secure code review by vulnerability class — Injection

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-001, SEC-002, SEC-004, SEC-005. Trace every string that reaches a query, shell,
or evaluator back to its origin. Concatenation and interpolation are the tell; the fix is always
the same shape — keep code and data separate via a binding API.

```python
# fail SEC-001: f-string SQL with request input
cur.execute(f"SELECT * FROM users WHERE email = '{email}'")
# pass: parameter binding
cur.execute("SELECT * FROM users WHERE email = %s", (email,))
```

```python
# fail SEC-002: shell string with external input
subprocess.run(f"convert {filename} out.png", shell=True)
# pass: argument array, no shell
subprocess.run(["convert", filename, "out.png"])
```

Review cues: ORM escape hatches (`raw()`, `text()`, native query annotations) get the same
scrutiny as raw SQL. Identifiers (table/column names) cannot be bound — require a hardcoded
allowlist map. For SEC-004, any `eval`/`exec`/`Function` on external input fails outright, and
user input handed to a template engine must arrive as a data parameter, never as template
source. For SEC-005, check LDAP/XPath/NoSQL filter construction, and reject regexes with nested
unbounded quantifiers like `(a+)+` applied to external input (CWE-1333 ReDoS).
