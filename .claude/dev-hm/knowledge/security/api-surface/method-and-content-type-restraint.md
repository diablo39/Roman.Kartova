# API surface protection — Method and content-type restraint

Section of `knowledge/security/api-surface.md`.


Routes register the exact methods they serve; there are no catch-all method handlers on
resource routes. An unregistered method returns 405, not a handler execution and not a 404 that
lies about the resource. Request bodies are accepted only in content types the endpoint
declares — typically one — and anything else is refused with 415 before deserialization is
attempted. Content-type restraint is also request-forgery relevant: an endpoint that accepts
`text/plain` and parses it as JSON re-opens the no-preflight cross-site window that
`application/json` enforcement closes (`knowledge/security/browser-protections.md`).

Verification tests: an unregistered method on each route class returns 405 with no side effect;
a correct body under a wrong `Content-Type` returns 415 and the deserializer is never entered
(assert no side effect, or assert the handler spy was not called).
