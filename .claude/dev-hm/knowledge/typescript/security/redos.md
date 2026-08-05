# TypeScript / web security patterns — Regular expression denial of service (ReDoS) {#redos}

Section of `knowledge/typescript/security.md`.


A regex with superlinear backtracking, run against attacker-controlled input, can take seconds to
minutes on a short crafted string — and Node's engine runs it on the event loop, so one request
stalls every request (`node-backend.md`). Risk shapes: nested quantifiers (`(a+)+`), overlapping
alternation under a quantifier (`(a|ab)*`), and adjacent unbounded classes that can match the same
text (`\s*\s*$`).

```ts
// Superlinear: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaa!" makes this backtrack exponentially
const ok = /^(\w+\s?)*$/.test(input);
// Fixed: unambiguous structure, and input length capped before the test
const ok = input.length <= 256 && /^\w+(?:\s\w+)*$/.test(input);
```

- Cap length before any regex touches untrusted input — the schema already does this when fields
  carry `.max(n)` (see validation above); a length cap turns worst-case blowup into microseconds.
- Prefer string methods or a real parser over a complex regex; most "validate this format" cases
  are schema formats (`z.email()`, `z.uuid()`) rather than hand-written patterns.
- Lint for it: `eslint-plugin-regexp`'s `no-super-linear-backtracking` catches these statically;
  a standalone analyzer (recheck) can scan an existing codebase.
- Never compile a user-supplied pattern with the built-in engine (`new RegExp(userInput)`) — that
  hands the attacker the exponent. If user patterns are a product feature, use a linear-time
  engine (an RE2 binding) with length caps, or run the match in a worker with a deadline.
