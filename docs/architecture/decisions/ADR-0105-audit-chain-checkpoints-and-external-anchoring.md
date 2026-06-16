# ADR-0105: Audit-Chain Checkpoints and External Anchoring

**Status:** Accepted
**Date:** 2026-06-16
**Deciders:** Roman Głogowski (solo developer)
**Category:** Security / Compliance
**Related:** ADR-0018 (audit log tamper-evidence + insert-only), ADR-0090 (tenant scope), ADR-0099 (Postgres advisory locks), ADR-0001 (PostgreSQL + RLS)

## Context

The audit log is a per-tenant SHA-256 hash chain: every row stores `prev_hash` (the prior row's
`row_hash`) and `row_hash` (a hash over its own canonical fields + `prev_hash`). `UPDATE`/`DELETE`
are revoked at the database (insert-only, ADR-0018), and RLS scopes every read/write to one tenant.
Verification re-walks the chain from genesis, recomputing each hash and checking the links.

Two problems surface as chains grow and as the regulator-facing verification surface (deferred to a
later phase) becomes real:

1. **Verification cost is O(n) in the whole chain.** A tenant with millions of audit rows pays a
   full-table read + N hash recomputations on every integrity check. The streaming verifier keeps
   *memory* flat, but wall-clock and I/O still scale linearly. Routine "is this tenant's chain
   intact?" checks cannot afford a full walk every time.

2. **The pure hash chain does not detect whole-prefix loss.** Modification and reordering are caught
   (a changed row breaks its own hash; a removed middle row breaks seq contiguity). But a chain that
   is *truncated to empty*, *rolled back to an old backup*, or *restored to a shorter earlier state*
   is still internally consistent — a shorter intact chain verifies as "OK". Nothing inside the DB's
   own trust boundary can prove "there used to be more rows than there are now," because the evidence
   and the thing it attests to live under the same write authority.

## Decision

Introduce **checkpoints** as a two-tier mechanism. Tier 1 ships now; Tier 2 is sketched here and
deferred until there is a verification endpoint to hang it on.

### Tier 1 — In-DB checkpoint (performance + cheap tamper-evidence) — *implement now*

A new insert-only, RLS-scoped `audit_checkpoint` table records snapshots of a tenant's chain head:

| Column | Type | Meaning |
|--------|------|---------|
| `id` | uuid (v7) | PK |
| `tenant_id` | uuid | RLS scope; unique with `seq` |
| `seq` | bigint | the chain seq this checkpoint attests to |
| `row_hash` | bytea(32) | the `row_hash` of the audit row at `seq` |
| `created_at` | timestamptz | when the checkpoint was taken |

- **Same trust model as the chain:** `audit_checkpoint` gets the *identical* treatment as `audit_log`
  — `ENABLE`/`FORCE ROW LEVEL SECURITY`, a `tenant_isolation` policy, and `REVOKE UPDATE, DELETE,
  TRUNCATE` from `kartova_app` and `kartova_bypass_rls`. A checkpoint, once written, can never be
  altered or removed by application code. No new key material and no signing at this tier — that
  would require a secret outside the DB, which is precisely Tier 2.
- **A checkpoint is only ever written over a verified-intact prefix.** Creation verifies the tail
  from the previous checkpoint (or genesis) to the current head; if the chain is broken, no
  checkpoint is written and the broken result is returned. So a checkpoint is a *memo that the prefix
  `1..seq` was intact and headed by `row_hash` at `created_at`*.
- **Verify-from-checkpoint algorithm (the fast path):**
  1. Load the latest checkpoint for the tenant (highest `seq`). None → full walk from genesis.
  2. Confirm `checkpoint.row_hash` equals the `row_hash` of the live `audit_log` row at
     `checkpoint.seq` (one indexed lookup on `ux_audit_log_tenant_seq`). Mismatch or missing row →
     **broken** (fabricated/inconsistent checkpoint, or the attested head was tampered/lost).
  3. Seed an `AuditChainWalker` at `(checkpoint.seq + 1, checkpoint.row_hash)` and stream-verify only
     the tail to the head.
  This is sound because audit rows are immutable (`UPDATE` revoked): if the row at `seq` still hashes
  to the checkpoint's value and the prefix was intact when checkpointed, the prefix is still intact.
  Cost drops from O(n) to O(rows since last checkpoint) + one indexed read.
- **Full verification stays available** (walk from genesis, ignore checkpoints) for deep audits and
  for regenerating trust after a suspected compromise. Routine checks use the fast path; regulator /
  incident checks can force the full path.
- **Concurrency:** checkpoint creation does not need the per-tenant append advisory lock — it captures
  a valid prefix snapshot whether or not a concurrent append advances the head (the snapshot is just
  slightly stale, still valid). A unique `(tenant_id, seq)` constraint rejects duplicate checkpoints
  if two creators race on the same head.
- **Cadence is a policy decision, not baked into the mechanism.** The writer/service exposes
  "create a checkpoint now"; *when* to call it (every N appends, a `LeaderElectedPeriodicService` per
  ADR-0099, or on-demand before a verify) is configured separately and can start as a simple periodic
  job.

### Tier 2 — External anchor (rollback / truncation evidence) — *deferred, sketched only*

The in-DB checkpoint defends against application-level tampering (insert-only + RLS). It does **not**
defend against an actor with DB-admin authority who can drop policies, re-grant, and rewrite both the
chain and its checkpoints — including truncating the whole tenant chain. Detecting that requires
evidence held **outside the database's trust boundary**:

- Periodically export the latest checkpoint `(tenant_id, seq, row_hash, created_at)` to an
  append-only / WORM store outside Postgres — candidates: MinIO/S3 bucket with **object-lock
  (compliance mode)** (we already run MinIO), an HMAC/asymmetric signature with a KMS-held key the DB
  role cannot read, or a third-party notarization/transparency log.
- Verification then additionally checks: *is the live chain at least as long as the newest anchored
  checkpoint, and does the live row at the anchored `seq` still match the anchored `row_hash`?* A
  shorter or mismatched chain → **rollback/truncation alarm**, which the pure chain cannot raise.

Tier 2 is deferred because it introduces key management and/or an external WORM dependency that
should be justified on its own (per the project's "justify added infrastructure" rule), and there is
no regulator-facing verification surface yet to consume the alarm. Tier 1 is a strict prerequisite
and stands alone.

## Consequences

### Positive

- Routine verification drops from O(whole chain) to O(tail since last checkpoint) + one indexed read.
- Truncation/rollback within app authority is caught at Tier 1 (checkpoint rows are insert-only, so a
  shorter live chain than an existing checkpoint is detectable); full DB-admin-level rollback becomes
  detectable once Tier 2 anchors evidence outside the DB.
- No new runtime infrastructure for Tier 1 — reuses Postgres, RLS, the existing grant model, and the
  `AuditChainWalker`. Checkpoints inherit the exact tamper-evidence guarantees of the chain.
- The fast path and full path share one walker, so they cannot disagree about what "intact" means.

### Negative / trade-offs

- The fast path *trusts* the latest checkpoint's prefix rather than re-deriving it, relying on the
  insert-only guarantee. A deep audit must still pay the full walk; this is a deliberate
  performance/assurance split, documented as such.
- A second insert-only table is added to the audit schema (migration + grants + RLS policy +
  integration coverage for the grant/RLS semantics).
- Tier 2's protection is only as strong as the external store's immutability and key custody; until
  Tier 2 ships, full DB-admin-level rollback of an isolated tenant remains undetectable.

### Upgrade path

Tier 1 is forward-compatible with Tier 2: anchoring consumes the same `audit_checkpoint` rows it
already produces, so enabling Tier 2 is additive (an exporter + an extra verification check), with no
schema change to the checkpoint table.
