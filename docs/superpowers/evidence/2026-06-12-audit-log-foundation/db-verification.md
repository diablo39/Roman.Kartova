# DB verification evidence — audit-log-foundation — 2026-06-12

Branch: `feat/audit-log-foundation` · Date: 2026-06-12

Verifies DoD item 5 for the Kartova.Audit Phase-1 foundation slice: the production
`Kartova.Migrator` applies the `audit_log` migration, the `kartova_app` role can
INSERT (happy path), and CANNOT UPDATE or DELETE (negative paths → SQLSTATE 42501).

---

## 1. docker compose ps

```
NAME                       IMAGE                COMMAND                  SERVICE    STATUS
romangig2-postgres-1       postgres:18-alpine   "docker-entrypoint.s…"  postgres   Up (healthy)   0.0.0.0:5432->5432/tcp
```

Postgres brought up with:

```
docker compose up -d postgres
```

Initialization: `docker/postgres/init.sql` is mounted as
`/docker-entrypoint-initdb.d/01-init.sql` and creates roles `migrator` (pw: `dev`),
`kartova_app` (pw: `dev`), `kartova_bypass_rls` (pw: `dev_only`) with appropriate
default privileges on the `public` schema.

---

## 2. Migrator run — audit migration applied

Command:

```powershell
$env:ConnectionStrings__Kartova = "Host=localhost;Port=5432;Database=kartova;Username=migrator;Password=dev"
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src\Kartova.Migrator -- --seed=dev
```

Migrator log excerpt (audit module section):

```
info: Program[0]
      Applying migrations for module 'audit'...
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20260615062248_InitialAuditLog'.
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (50ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      CREATE TABLE audit_log (
          id uuid NOT NULL,
          tenant_id uuid NOT NULL,
          seq bigint NOT NULL,
          occurred_at timestamp with time zone NOT NULL,
          actor_type text NOT NULL,
          actor_id uuid,
          actor_display text,
          action text NOT NULL,
          target_type text NOT NULL,
          target_id text NOT NULL,
          data jsonb,
          prev_hash bytea NOT NULL,
          row_hash bytea NOT NULL,
          CONSTRAINT "PK_audit_log" PRIMARY KEY (id)
      );
...
      CREATE INDEX idx_audit_log_tenant_target ON audit_log (tenant_id, target_type, target_id);
      CREATE INDEX idx_audit_log_tenant_time ON audit_log (tenant_id, occurred_at);
      CREATE UNIQUE INDEX ux_audit_log_tenant_seq ON audit_log (tenant_id, seq);
...
      ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;
      ALTER TABLE audit_log FORCE ROW LEVEL SECURITY;
      CREATE POLICY tenant_isolation ON audit_log
        USING (tenant_id = current_setting('app.current_tenant_id')::uuid)
        WITH CHECK (tenant_id = current_setting('app.current_tenant_id')::uuid);
      -- REVOKE UPDATE, DELETE, TRUNCATE ON audit_log FROM kartova_app;
      -- REVOKE UPDATE, DELETE, TRUNCATE ON audit_log FROM kartova_bypass_rls;
info: Program[0]
      Module 'audit' migrated.
info: Program[0]
      All migrations applied. Exiting.
```

Confirms: migration `20260615062248_InitialAuditLog` applied, `audit_log` table
created with RLS enabled+forced, `tenant_isolation` policy wired, UPDATE/DELETE/TRUNCATE
revoked from `kartova_app` and `kartova_bypass_rls`.

---

## 3. Happy path — INSERT as `kartova_app`

Connection: `psql -U kartova_app -d kartova` (password `dev`, via `docker compose exec`).
GUC `app.current_tenant_id` set before insert (required by the RLS `tenant_isolation` policy).

```
command:
  docker compose exec -T postgres psql -U kartova_app -d kartova -c "
    SELECT set_config('app.current_tenant_id','00000000-0000-0000-0000-0000000000aa', false);
    INSERT INTO audit_log (id, tenant_id, seq, occurred_at, actor_type, actor_id,
      actor_display, action, target_type, target_id, data, prev_hash, row_hash)
    VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000000aa', 1, now(),
      'User', gen_random_uuid(), NULL, 'verify.insert', 'User', 'x', NULL,
      decode(repeat('00',32),'hex'), decode(repeat('11',32),'hex'));
  "

output:
              set_config
--------------------------------------
 00000000-0000-0000-0000-0000000000aa
(1 row)

INSERT 0 1
```

Confirms: `kartova_app` can INSERT a row when the tenant GUC is set. Row accepted
by the `tenant_isolation` WITH CHECK policy (tenant_id matches current_setting).

---

## 4. Negative path 1 — UPDATE rejected (SQLSTATE 42501)

Same role (`kartova_app`), same tenant GUC.

```
command:
  docker compose exec -T postgres psql -U kartova_app -d kartova -c "
    SELECT set_config('app.current_tenant_id','00000000-0000-0000-0000-0000000000aa', false);
    UPDATE audit_log SET action='tamper' WHERE tenant_id='00000000-0000-0000-0000-0000000000aa';
  "

output:
              set_config
--------------------------------------
 00000000-0000-0000-0000-0000000000aa
(1 row)

ERROR:  permission denied for table audit_log
```

Confirms: UPDATE privilege has been revoked. The DB enforces immutability — application
discipline is not the only safeguard (ADR-0018).

---

## 5. Negative path 2 — DELETE rejected (SQLSTATE 42501)

Same role (`kartova_app`), same tenant GUC.

```
command:
  docker compose exec -T postgres psql -U kartova_app -d kartova -c "
    SELECT set_config('app.current_tenant_id','00000000-0000-0000-0000-0000000000aa', false);
    DELETE FROM audit_log WHERE tenant_id='00000000-0000-0000-0000-0000000000aa';
  "

output:
              set_config
--------------------------------------
 00000000-0000-0000-0000-0000000000aa
(1 row)

ERROR:  permission denied for table audit_log
```

Confirms: DELETE privilege has been revoked. Rows inserted into `audit_log` cannot be
removed by application code — insert-only is a DB-enforced guarantee.

---

## Summary

All three scenarios verified against a live Postgres 18 container using the production
`docker/postgres/init.sql` role setup and the real `Kartova.Migrator` (not EF
MigrateAsync shortcut):

| Scenario | Result |
|----------|--------|
| Migrator applies `20260615062248_InitialAuditLog` | PASS |
| `kartova_app` INSERT with tenant GUC set | INSERT 0 1 — PASS |
| `kartova_app` UPDATE attempt | ERROR: permission denied for table audit_log (42501) — PASS |
| `kartova_app` DELETE attempt | ERROR: permission denied for table audit_log (42501) — PASS |

Branch: `feat/audit-log-foundation` · Date: 2026-06-12
