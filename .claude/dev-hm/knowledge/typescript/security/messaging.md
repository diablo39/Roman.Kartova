# TypeScript / web security patterns — Client-side messaging and navigation targets {#messaging}

Section of `knowledge/typescript/security.md`.


Two web-specific channels bypass the JSX escaping above and need their own guard.

- `postMessage`: send with a specific target origin, never `"*"`, when the payload is not public, or
  a malicious embedder receives it. In every `message` handler, check `event.origin` against an
  allowlist before trusting `event.data` — an unchecked handler accepts messages from any frame.
- Navigation targets built from user input — `href`, `src`, `formAction`/`action`, `window.open`,
  `location.assign`/`location.href` — can carry `javascript:` or `data:` URLs. Allowlist the scheme
  (`https:`, `mailto:`, relative) before assigning; reject the rest.
- Cross-origin `target="_blank"` links and `window.open` include `rel="noopener"` (and usually
  `noreferrer`) so the opened page cannot reach back through `window.opener`.
