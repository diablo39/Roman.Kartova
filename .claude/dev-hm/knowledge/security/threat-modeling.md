# Threat modeling scaled to a code change

Lightweight STRIDE-based modeling for reviewing a diff or a feature — minutes, not workshops.
The security oracle (`oracles/security-oracle.md`) checks known-bad patterns deterministically;
threat modeling finds the risks the oracle cannot see: missing controls, new trust relationships,
and design flaws (OWASP Top 10:2025 A06 Insecure Design has no oracle entry for exactly this
reason). Output feeds the oracle run: it tells you which entries apply and where to look hardest.

## When to threat model a change

Model when the diff does any of the following; otherwise skip straight to the oracle run and note
"no boundary change" in the report.

| Trigger | Why it matters |
|---|---|
| New endpoint, handler, message consumer, or CLI entry point | New attack surface; authn/authz decisions being made |
| New external input (parameter, header, file, webhook, queue message) | New data crossing a trust boundary |
| New outbound integration (API, database, queue, third-party SDK) | New trust extended to an external system |
| Changes to authentication, authorization, session, or crypto code | Highest-value target; regressions are S0 territory |
| New secret, credential, or key material | Storage, rotation, and exposure questions |
| New tenant-, role-, or ownership-related logic | Object- and function-level authorization risk |
| New file, path, URL, or template handling | Injection and traversal classes activate |
| Privilege changes in deployment or CI config | Supply chain and lateral-movement risk |

## Trust boundaries

A trust boundary is any line where the level of trust in data or callers changes. Enumerate the
boundaries the diff touches before asking what can go wrong; every finding attaches to a boundary.

Common boundaries in application code:

- Internet → service edge (browser, mobile app, API client → handler)
- Service → service (internal RPC, queue consumer — internal does not mean trusted)
- Service → data store (query construction is a boundary crossing)
- Service → third party (outbound calls, webhooks, OAuth providers)
- User content → interpreter (templates, HTML rendering, file parsers, deserializers)
- Repository → build pipeline (dependencies, CI actions, build scripts)
- Process → host (file paths, environment, subprocess invocation)

For each boundary the diff touches, record one line: what crosses it, in which direction, and
which control guards it (authentication, schema validation, encoding, allowlist). A crossing
with no named control is a finding.

## STRIDE-lite

Ask six questions per touched boundary. Skip categories that obviously do not apply; write down
the answer only when it is "yes" or "unclear".

| Letter | Threat | Question for this diff | Typical oracle entries |
|---|---|---|---|
| S | Spoofing | Can a caller pretend to be someone else — missing or weakened authentication, forgeable tokens, unauthenticated consumers? | SEC-010, SEC-013 – SEC-016 |
| T | Tampering | Can data be altered in flight or at rest — unvalidated input, mass assignment, unsigned artifacts, hand-edited generated files? | SEC-040, SEC-044, SEC-060 – SEC-065 |
| R | Repudiation | Can an actor deny an action — security events without actor/outcome logging, missing correlation IDs? | SEC-080 – SEC-082 |
| I | Information disclosure | Can data leak — verbose errors, secrets in logs or URLs, overly broad query results, missing object-level checks? | SEC-011, SEC-020 – SEC-023, SEC-072, SEC-082 |
| D | Denial of service | Can this be made to exhaust resources — unbounded parsing, missing timeouts, unbounded queues, regex backtracking? | SEC-005, SEC-052, QUA-041, QUA-072 |
| E | Elevation of privilege | Can a caller gain rights — missing function-level checks, fail-open error paths, injection into an interpreter? | SEC-001 – SEC-005, SEC-012, SEC-050, SEC-071 |

Two habits keep this deterministic enough for review:

- Anchor every threat to a concrete code location (`file:line`) and a boundary. "Someone could
  attack the API" is not a threat; "POST /orders accepts `user_id` in the body and uses it for
  the ownership check (orders.py:88) — spoofing via caller-supplied identity" is.
- State the attacker for each threat: anonymous internet caller, authenticated low-privilege
  user, tenant admin, compromised dependency, malicious insider with repo access. A threat
  without a plausible attacker is noise.

## The fifteen-minute method

1. Scope. List entry points, outputs, and external interactions the diff adds or changes. One
   line each.
2. Draw boundaries. From the list above, name the boundaries touched. A mental model or a
   five-box Mermaid sketch is enough; do not produce a document for a two-file diff.
3. Run STRIDE-lite per boundary. Record only yes/unclear answers, each with location and
   attacker.
4. Decide disposition for each recorded threat:
   - Covered by an oracle entry → run that entry with extra attention; cite it.
   - Real but not oracle-covered → report as a `finding` with a severity from
     `knowledge/shared/severity-tiers.md`.
   - Design-level (wrong trust relationship, missing control that needs architecture) → report
     at its true severity with the boundary description and the architectural change it implies;
     never downgrade it because the fix is large, and never silently accept it.
5. Feed the oracle run. The threats you recorded determine which SEC-* sections get the closest
   read and which files you open beyond the diff.

## Recording the result

The threat-model note travels in the review or gate report, before the verdict table. Keep it
under ten lines for a typical change:

```
Threat model: 2 boundaries touched
- Internet -> POST /webhooks/payment (new): HMAC signature check present (webhook.py:41);
  spoofing covered by SEC-010 run; replay unhandled -> finding S2 (no timestamp/nonce check)
- Service -> billing API (new outbound): TLS + timeout present; SSRF n/a (static host)
Escalations: none
```

An empty model is also a result: "Threat model: no trust boundary touched (internal refactor,
no new inputs/outputs)" — it tells the gate the question was asked, not skipped.

## Scaling up

When a change keeps triggering escalations — several new boundaries, a new service, a new tenant
model — the fifteen-minute method is the wrong tool. Hand the feature to solution-architect for a
full design review with an explicit data-flow diagram, and record the outcome as an ADR
(`knowledge/architecture/adr-practice.md`). The code-review threat model then verifies the
implementation matches the agreed design instead of re-deriving it.
