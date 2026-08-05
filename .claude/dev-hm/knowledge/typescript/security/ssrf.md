# TypeScript / web security patterns — SSRF and open redirects {#ssrf}

Section of `knowledge/typescript/security.md`.


- A server `fetch` to a user-supplied URL is an SSRF sink; allowlist hosts/schemes and block
  internal ranges and metadata endpoints rather than denylisting.
- A redirect target read from user input (`?next=`) must be validated against an allowlist of
  internal paths, never reflected verbatim.
