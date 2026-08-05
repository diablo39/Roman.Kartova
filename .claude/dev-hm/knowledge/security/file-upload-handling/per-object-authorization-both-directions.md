# File upload handling — Per-object authorization, both directions

Section of `knowledge/security/file-upload-handling.md`.


Uploads are objects, and SEC-011 applies to both ends of their life:

- Write direction: attaching an upload to a resource (an order, a ticket, a profile) verifies
  the caller's right to that resource — otherwise any authenticated user can plant files on
  records they cannot read.
- Read direction: every download handler verifies the caller's right to that specific object.
  Possession of the object identifier is not a right to it; generated names make URLs hard to
  guess, which is obscurity, not authorization.

Where downloads are delegated to an object store, the handler authorizes and then issues a
short-lived signed URL scoped to the single object; long-lived or bucket-wide URLs recreate the
unauthenticated-download problem one layer down. The authorization patterns and tests are the
ownership patterns in `knowledge/security/control-test-patterns-access.md`, with the upload as
the owned object.

Serving headers, whichever path serves the bytes: the response carries the content type we
validated and stored — never one derived from the request — plus `X-Content-Type-Options:
nosniff`, and `Content-Disposition: attachment` with a sanitized filename for anything not
explicitly meant to render inline. Authenticated downloads follow the cache rule in
`knowledge/security/browser-protections.md#response-cache-hygiene`.
