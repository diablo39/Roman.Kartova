# Deep Review — Infrastructure/VM Slice 2a (fields + mutation)

**Reviewer:** deep-review (gate 8), single in-session reviewer
**Diff:** `a655666..361a710` (full branch `feat/catalog-infrastructure-vm-slice2a`)
**Spec:** `docs/superpowers/specs/2026-09-09-infrastructure-vm-slice2a-fields-mutation-design.md`
**Plan:** `docs/superpowers/plans/2026-09-09-infrastructure-vm-slice2a-fields-mutation.md`
**Status:** OPEN (pre-merge gate) · **Date:** 2026-09-09

Prior gates already cleared and NOT re-litigated here: per-task reviews, whole-branch opus review, `/simplify`, `/review-pr` (5 reviewers — fixed the VM-attribute-400 silent failure `attributes.<field>`, the AllInfrastructure provider-sort no-op, a RegisterVmDialog cast, concurrency-capture logging, provider empty→null normalization, test/comment gaps in commit `361a710`). Known-deferred: `RegisterVmDialog` teamId `<select>` value-registration (pre-existing slice-1 pattern) → gate-9 browser check.

---

### Overview

Slice 2a completes the VM entity's write + query surface on top of ADR-0115's slice-1 read/create: a shared-typed `provider varchar(256)` column (surfaced on both list tiers, both detail views, create + edit forms; column + sort, filter deferred), full `PUT` update and OrgAdmin-only hard `DELETE` with RFC 7232 `If-Match` optimistic concurrency (428 missing / 412 stale, `currentVersion` hint), an ETag/`version` on the VM single-GET, and full JSONB-attribute sort (`powerState/os/vcpu/memoryGb/hostname/region`) via a separate `VmSortField`/`VmSortSpecs` tier backed by 6 partial btree-expression indexes byte-identical to the EF-emitted `ORDER BY`. Adds the `catalog.infrastructure.delete` permission (complete 5-sync). No relationship edges (slice 2b). 4331 insertions across 67 files.

### Blocking-class issues

None.

Every spec §3 locked decision (#1 hard delete + new perm, #2 provider column, #5 full JSONB sort via 6 partial indexes, #6 separate `VmSortField`, #7 ETag/version, #8 PUT full-replace → 412) is implemented as designed; the two ADR-0115 deferrals (JSONB-column sort, `provider`) are explicitly superseded by this spec. Concurrency status codes (428/412, never 409), the `Xmin` concurrency token, RLS-scoped loads (cross-tenant → 404), the permission 5-sync, and camelCase wire (ADR-0109) all conform. No DoD gate is failed by a code defect.

### Should-fix issues

- **JSONB sort selectors have no missing-key / null guard — latent null-keyset truncation (same class as the Provider COALESCE fix).**
  - **Evidence:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/VmSortSpecs.cs` (PowerState/Os/Hostname/Region use `JsonbFunctions.JsonbExtractPathText(x.Attributes, "…")!`; Vcpu/MemoryGb use `Convert.ToInt32(JsonbFunctions.JsonbExtractPathText(…))`). Contrast the deliberate fix on the sibling column: `InfrastructureSortSpecs.cs` `Provider = new("provider", x => x.Provider ?? "")` whose own comment states *"a NULL boundary value makes the shared keyset predicate evaluate to SQL UNKNOWN and silently truncates pagination."*
  - **Impact:** Latent, not reachable through today's API — `VmAttributes.Validate` guarantees all six keys are present and non-null on every create/edit, so no null boundary can arise now. But the attributes payload is explicitly opaque and slice-4 auto-import will write rows *without* going through `VmAttributes.Validate` (spec §3 #4 names auto-import as the field-set driver). A VM row missing a sorted key then yields client-side `null` (text) or `0` (numeric via `Convert.ToInt32(null)`) while Postgres yields `NULL` — reintroducing exactly the keyset truncation the Provider COALESCE was added to prevent, and additionally an int-cast asymmetry (0 vs NULL).
  - **Fix:** COALESCE the six selectors to a stable sentinel now (text → `?? ""`, numeric → a documented default), mirroring `InfrastructureSortSpecs.Provider`; or record and test the "attribute always present" precondition as an enforced invariant so slice-4 import inherits it. Cheapest belt-and-braces: `JsonbFunctions.JsonbExtractPathText` already returns `null` for a missing/null key, so wrapping each selector `?? ""` / a numeric guard is a one-line-per-field change.

- **DoD ledger and `gate-findings.yaml` are stale — every gate ⏳ PENDING and `findings: []`, while the branch shows gates ran and fixed real issues.**
  - **Evidence:** `docs/superpowers/verification/2026-09-09-infrastructure-vm-slice2a/dod.md` (all 11 rows ⏳ PENDING, placeholder evidence) and `gate-findings.yaml` (`findings: []`) — yet commit `361a710` is literally *"fix(catalog): gate-7 review — surface VM validation errors, AllInfrastructure provider sort, concurrency logging, tests"* and the branch also carries `/simplify` (`27cc450`) and int-cast-index verification (`fa76a97`) commits.
  - **Impact:** Violates CLAUDE.md DoD ("update each gate's row the moment that gate runs" + "`gate-findings.yaml` records what each gate found"). A "what's the DoD status?" query returns "nothing run / nothing found," which is demonstrably false — gate 7 found and fixed ≥6 real findings. Traceability of the gate that caught the silent-400 bug is lost.
  - **Fix:** Backfill `dod.md` rows 1–7 (+ terminal re-verify) with their command/CI evidence and mark this deep-review as gate 8; log the gate-5/6/7 findings (severity · real/delusion · fix sha) in `gate-findings.yaml`. The `dod-check.js` stop hook will otherwise block the completion claim.

### Nits

- **Stale EF-config comment asserts a spec deviation that no longer exists.** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfInfrastructureConfiguration.cs` — the `Provider` mapping comment says *"Deliberate deviation from spec §3 #2 / §5.2 ('provider text NULL')"*, but the spec was reconciled (commit `f980b51`) and §3 #2 / §5.2 now both specify `varchar(256)`. There is no deviation; the comment is rot. Trim to "varchar(256) enforces the 256-char cap at the DB level (spec §3 #2)."
- **`EditVmAsync` loads the VM twice per edit.** `CatalogEndpointDelegates.cs` `EditVmAsync` loads via `VmSortSpecs.IdEquals(id)` to authorize against `vm.TeamId`, then `EditVmHandler.Handle` loads the same row again (`VmSortSpecs.IdEquals(cmd.Id.Value)`). Same scoped DbContext so it's one identity-map hit + one extra round-trip, not a correctness issue (verified: the handler's `OriginalValue(Xmin) = ExpectedVersion` override makes the concurrency check correct regardless of the delegate load), but it is a redundant query on the write path (ADR-0075 p95 write <500ms). Consider authorizing inside the handler or threading the loaded entity through.

### Missing tests

- **Audit-entry write on edit/delete (spec §6.2: "Both write an `IAuditWriter` entry").** No unit or integration test asserts that `infrastructure.edited` / `infrastructure.deleted` audit rows are appended (grep of the diff finds the two new `CatalogAuditActions` consts and the two `audit.AppendAsync` call sites, but zero assertions). Add to `InfrastructureVmWriteTests`: after a successful `PUT`, assert an audit row exists with action `infrastructure.edited`, target-type `Infrastructure`, target-id = VM id, and `provider` in the payload; after a successful `DELETE`, the same for `infrastructure.deleted` (mirror the slice-1 register-audit assertion pattern).
- **Partial-index-use assertion for `os`/`hostname`/`region` JSONB sorts.** `InfrastructureVmSortTests.ListVms_sortBy_jsonbField_emits_literal_key_and_uses_partial_index` EXPLAIN-covers only `powerState` (text), `vcpu`, `memoryGb` (int). `os`/`hostname`/`region` are index-verified only by structural analogy to `powerState`. Add DataRows for `os → ix_catalog_infrastructure_vm_os`, `hostname → …_hostname`, `region → …_region` so a future byte-mismatch on any of the three text indexes fails (no `Seq Scan`) rather than silently regressing to a seq-scan.

### What looks good

- **JSONB expression-index discipline is exemplary.** `JsonbFunctions.JsonbExtractPathText([NotParameterized] string key)` (`JsonbFunctions.cs`) plus `HasDbFunction(...).IsBuiltIn()` (`CatalogDbContext.cs`) keeps the key literal a `Const` node so the EF `ORDER BY` stays byte-identical to the migration DDL; the class body also mirrors Postgres semantics for the client-side cursor-boundary compile path, and the `[NotParameterized]` rationale is captured with a named regression test — a genuinely subtle perf-correctness seam handled well.
- **Provider is a single source of truth across tiers.** `VmSortSpecs.Provider` re-exports `InfrastructureSortSpecs.Provider` (same `SortSpec` instance) rather than re-declaring it, so the generic and VM tiers cannot diverge on ordering or null-handling — the ADR-0115 two-tier boundary is honored without duplication (`VmSortSpecs.cs`, `InfrastructureSortSpecs.cs`).
- **Concurrency capture is shared, safe, and observable.** `InfrastructureConcurrency.TryCaptureCurrentXminAsync` (extracted once a second caller appeared) reads `Xmin` while the tenant connection is still alive, swallows capture failure without masking the real `DbUpdateConcurrencyException`, excludes `OperationCanceledException`, and logs a warning so a broken connection-lifetime assumption is visible — and the `currentVersion` hint is asserted end-to-end for both PUT and DELETE (`InfrastructureVmWriteTests` gate-7 T2).
- **Negative-path integration coverage is thorough and hits the real seam.** PUT and DELETE each cover 412 (with `currentVersion` equality), 428, 403, 404, and RLS cross-tenant → 404; `Delete_NonOrgAdmin_Returns403` uses a Member carrying `register` but not `delete` to prove the new permission is a distinct, narrower gate — exactly the decision-#1 intent.
- **Permission 5-sync is complete and correctly OrgAdmin-scoped.** All five touchpoints present (`KartovaPermissions` const+`All`, `KartovaRolePermissions` OrgAdmin-only, `permissions.snapshot.json`, `permissions.ts`, `usePermissions.test.tsx`), matching the destructive-op / reverse-lifecycle precedent, with `VmDetailPage` gating Edit on `register` and Delete on `delete` independently.
