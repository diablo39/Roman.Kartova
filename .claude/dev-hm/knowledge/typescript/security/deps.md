# TypeScript / web security patterns — Dependencies {#deps}

Section of `knowledge/typescript/security.md`.


- Keep a lockfile committed; install with `npm ci` (or the equivalent frozen-lockfile flag) in CI so
  builds are reproducible.
- Gate on `npm audit` (or the ecosystem scanner) at a defined severity; treat unreviewed
  postinstall scripts and typosquatted names as supply-chain risks.
