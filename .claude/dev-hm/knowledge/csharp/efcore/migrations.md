# EF Core patterns — Migrations for rolling deploys {#migrations}

Section of `knowledge/csharp/efcore.md`.


Rolling deploys run old and new application versions against the same database for minutes to
hours. Every migration must keep both versions working — the expand/contract discipline
(`knowledge/quality/data-migration-safety.md` for the general method,
`knowledge/postgresql/schema-design.md` for lock-safe DDL):

1. **Expand** (release N): add nullable columns/new tables/indexes; write to both old and new
   shapes if renaming; never drop or rename in the same release that introduces the replacement.
2. **Migrate data** (release N, background): backfill in batches; avoid one giant
   transaction that locks the table.
3. **Contract** (release N+1 or later): drop the old column/table once no running version reads it.

Operational rules:

- Apply migrations as an explicit deploy step — a migration bundle (`dotnet ef migrations bundle`)
  or an idempotent script (`dotnet ef migrations script --idempotent`) run by the pipeline before
  the new pods roll. Do not call `Database.MigrateAsync()` from application startup in
  multi-replica services: replicas race, and a failed migration takes the whole rollout down with
  partial DDL applied.
- `EnsureCreated` is for throwaway tests only — it bypasses migration history and cannot be
  migrated later.
- Review the generated migration code, not just the model diff: EF's scaffold can produce a
  table-rebuild or a blocking index build where an online operation exists. Hand-edit the migration
  (or add raw SQL) when the default DDL would lock a hot table.
- Migrations no longer run wrapped in one all-encompassing transaction on the current LTS; a
  multi-step migration must be written so a mid-point failure leaves a recoverable state.
- Down/rollback migrations are rarely safe once data has been written — plan roll-forward fixes,
  and treat the destructive contract step as the point of no return.
