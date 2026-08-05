# TypeScript / web security patterns — Secrets and environment exposure {#secrets}

Section of `knowledge/typescript/security.md`.


Client bundles are public. Anything a browser can reach — env vars without the framework's public
prefix, imported server modules, string literals — ships to every visitor.

- Only expose env vars through the framework's public prefix (`VITE_`, `NEXT_PUBLIC_`); everything
  else is server-only. A secret behind such a prefix is a leak.
- Never `import` a server-only module (DB client, secret reader, private SDK) into a client
  component or shared code reachable from the client. In RSC apps, keep secret access in Server
  Components / server actions and mark the boundary with `import "server-only"`.
- No hardcoded credential, token, private key, or connection string as a literal anywhere in the
  tree — provide via environment and validate at startup (see validation above).
- Verify with the bundle-analysis workflow in `debugging.md`: search the emitted client bundle for
  known secret prefixes and values before shipping.
