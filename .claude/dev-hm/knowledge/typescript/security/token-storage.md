# TypeScript / web security patterns — Authentication token storage {#token-storage}

Section of `knowledge/typescript/security.md`.


`localStorage` and `sessionStorage` are readable by any script on the page, so any XSS exfiltrates
tokens stored there. Access/refresh/session tokens belong in `HttpOnly`, `Secure`, `SameSite`
cookies set by the server — never in web storage or a non-`HttpOnly` cookie.

- Do not store JWTs, refresh tokens, session ids, or API keys via `localStorage.setItem` /
  `sessionStorage.setItem`.
- Short-lived access token + server-side refresh (rotating refresh token) limits the window if a
  token does leak.
- In-memory (a variable/closure) is acceptable for a short-lived access token in a SPA; storage that
  survives a reload must be an `HttpOnly` cookie.
