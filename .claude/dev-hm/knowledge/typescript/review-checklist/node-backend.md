# Node backend

| Sev | Finding | What to look for |
|---|---|---|
| S0 | String-built SQL with external input | Query assembled by concatenation/template with request data. Use parameterized queries or a typed query builder (`security.md`). |
| S1 | Unvalidated request input | `req.body`/`request.json()`/params used without schema validation at the boundary (`security.md`). |
| S1 | Blocking the event loop | Sync FS/crypto/`JSON` over large payloads on the request path; use async APIs or a worker. |
| S2 | Resource not released | DB connection/file handle/stream opened without a `finally` or `using` release. |
