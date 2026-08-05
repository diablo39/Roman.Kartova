# File upload handling — Active content: SVG and HTML

Section of `knowledge/security/file-upload-handling.md`.


SVG and HTML files are programs to a browser. An SVG served inline from the application origin
executes any script it carries with the session cookies and DOM of that origin — a stored-XSS
delivery vehicle wearing an image extension; the same holds for uploaded HTML. The rule:

- Tier 1 (default): do not accept SVG or HTML uploads where the feature works without them.
  Raster formats cover avatars, previews, and most content images, and re-encoding makes them
  inert.
- Where SVG is a requirement (logos, diagrams): sanitize with a maintained sanitizer configured
  for SVG, and serve the result from a separate origin that holds no session — a dedicated
  user-content domain — or, failing that, with `Content-Security-Policy: sandbox; script-src
  'none'` and `Content-Disposition: attachment` on the serving response. Sanitizer
  configuration gets a snapshot test, so a widened allowlist is a reviewed diff.
- Uploaded HTML is never served inline from the application origin; a preview feature renders
  it inside a sandboxed frame on the user-content origin or not at all.

The same lens applies to any format with an execution path in some consumer — the acceptance
decision names where the file will be rendered and by what, and the handoff records it.
