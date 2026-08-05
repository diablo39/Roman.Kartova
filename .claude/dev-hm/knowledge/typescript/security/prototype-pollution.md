# TypeScript / web security patterns — Prototype pollution {#prototype-pollution}

Section of `knowledge/typescript/security.md`.


Recursive merge, extend, and set-by-path operations over external input can write to
`__proto__`, `constructor`, or `prototype` and poison every object in the realm (CWE-1321,
oracle SEC-TS-011).

- Reject or skip those three keys in any deep merge/set-by-path that touches external input, or
  build the target from `Object.create(null)` / use a `Map`.
- Prefer a maintained, pollution-hardened utility over a hand-rolled deep merge; validate the
  input's shape with a schema first so unexpected keys never reach the merge.
