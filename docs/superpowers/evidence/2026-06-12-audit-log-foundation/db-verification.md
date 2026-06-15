# DB verification evidence — audit-log-foundation — 2026-06-12

Branch: `feat/audit-log-foundation` · Re-verified: 2026-06-15

Verifies DoD item 5 for the Kartova.Audit Phase-1 foundation slice: the production
`Kartova.Migrator` applies the `audit_log` migration, the `kartova_app` role can
INSERT (happy path), and CANNOT UPDATE or DELETE (negative paths → SQLSTATE 42501).

**Honesty note:** Section 2 contains the verbatim migrator log. The full log spans
catalog + organization + audit modules; lines for the catalog and organization modules
are elided with an explicit `[... lines elided ...]` marker. The audit module section
(lines from `Applying migrations for module 'audit'...` through
`Module 'audit' migrated.`) is reproduced in full, including the complete
`DO $$ BEGIN ... END $$;` REVOKE block exactly as EF Core logged it. Nothing is
paraphrased or commented out.

---

## 0. Clean-slate setup

```
docker compose down -v
docker compose up -d postgres
```

After `docker compose down -v` the postgres-data volume is destroyed; `init.sql`
re-runs on first container start and recreates the `migrator`, `kartova_app`, and
`kartova_bypass_rls` roles.

---

## 1. docker compose ps

```
NAME                   IMAGE                COMMAND                  SERVICE    CREATED         STATUS                   PORTS
romangig2-postgres-1   postgres:18-alpine   "docker-entrypoint.s…"   postgres   12 seconds ago  Up 11 seconds (healthy)  0.0.0.0:5432->5432/tcp, [::]:5432->5432/tcp
```

---

## 2. Migrator run — audit migration applied

Command:

```powershell
$env:ConnectionStrings__Kartova = "Host=localhost;Port=5432;Database=kartova;Username=migrator;Password=dev"
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src\Kartova.Migrator -- --seed=dev
```

Full migrator stdout/stderr (verbatim); catalog and organization sections elided:

```
info: Program[0]
      Kartova migrator starting; 3 module(s) registered.
info: Program[0]
      Applying migrations for module 'catalog'...

[... lines elided: catalog module migrations 20260421192803_InitialCatalog through 20260610133628_RealignApplicationOwnership ...]

info: Program[0]
      Module 'catalog' migrated.
info: Program[0]
      Applying migrations for module 'organization'...

[... lines elided: organization module migrations 20260423080230_InitialOrganization through 20260609194450_AddUserRealmRoleColumn ...]

info: Program[0]
      Module 'organization' migrated.
info: Program[0]
      Applying migrations for module 'audit'...
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (2ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "MigrationId", "ProductVersion"
      FROM "__EFMigrationsHistory"
      ORDER BY "MigrationId";
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT 1
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (2ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
          "MigrationId" character varying(150) NOT NULL,
          "ProductVersion" character varying(32) NOT NULL,
          CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
      );
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (2ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "MigrationId", "ProductVersion"
      FROM "__EFMigrationsHistory"
      ORDER BY "MigrationId";
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20260615062248_InitialAuditLog'.
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (6ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
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
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (3ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      CREATE INDEX idx_audit_log_tenant_target ON audit_log (tenant_id, target_type, target_id);
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (3ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      CREATE INDEX idx_audit_log_tenant_time ON audit_log (tenant_id, occurred_at);
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (3ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      CREATE UNIQUE INDEX ux_audit_log_tenant_seq ON audit_log (tenant_id, seq);
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (8ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      
      ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;
      ALTER TABLE audit_log FORCE ROW LEVEL SECURITY;
      
      -- Tenant isolation. USING gates SELECTs; WITH CHECK explicitly gates INSERTed rows so
      -- the insert-tenant constraint is self-documenting and matches spec §4.
      CREATE POLICY tenant_isolation ON audit_log
        USING (tenant_id = current_setting('app.current_tenant_id')::uuid)
        WITH CHECK (tenant_id = current_setting('app.current_tenant_id')::uuid);
      
      -- ADR-0018 insert-only: the app + bypass roles inherit SELECT,INSERT,UPDATE,DELETE from the
      -- migrator's default privileges (docker/postgres/init.sql). Strip every mutating privilege so
      -- an audit row can never be altered or removed by application code — the database, not app
      -- discipline, is the guarantee. Guarded so the migration also applies in environments where a
      -- role happens not to exist.
      DO $$
      BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kartova_app') THEN
          REVOKE UPDATE, DELETE, TRUNCATE ON audit_log FROM kartova_app;
        END IF;
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kartova_bypass_rls') THEN
          REVOKE UPDATE, DELETE, TRUNCATE ON audit_log FROM kartova_bypass_rls;
        END IF;
      END $$;
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (2ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
      VALUES ('20260615062248_InitialAuditLog', '10.0.2');
info: Program[0]
      Module 'audit' migrated.
info: Program[0]
      Dev seed: Org A inserted.
info: Program[0]
      Dev seed: team-admin@orga users row inserted.
info: Program[0]
      Dev seed: demo team inserted.
info: Program[0]
      Dev seed: demo team Admin membership for team-admin@orga inserted.
info: Program[0]
      Dev seed: inserted 120 applications for Org A.
info: Program[0]
      All migrations applied. Exiting.
```

Confirms: migration `20260615062248_InitialAuditLog` applied, `audit_log` table
created with RLS enabled+forced, `tenant_isolation` policy wired with both
`USING` and `WITH CHECK` clauses, and the `DO $$ BEGIN ... END $$;` block
executed live — both `kartova_app` and `kartova_bypass_rls` branches fired
(roles exist per `init.sql`). The REVOKE statements are inside an active PL/pgSQL
block, not SQL comment lines.

---

## 3. DB state corroboration

### 3a. Migration recorded in `__EFMigrationsHistory`

```
docker compose exec -T postgres psql -U migrator -d kartova -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%InitialAuditLog';"
```

Output:

```
          MigrationId           
--------------------------------
 20260615062248_InitialAuditLog
(1 row)
```

### 3b. Table privileges — UPDATE/DELETE absent for `kartova_app`

```
docker compose exec -T postgres psql -U postgres -d kartova -c "SELECT grantee, privilege_type FROM information_schema.role_table_grants WHERE table_name='audit_log' AND grantee IN ('kartova_app','kartova_bypass_rls') ORDER BY grantee, privilege_type;"
```

Output:

```
      grantee       | privilege_type 
--------------------+----------------
 kartova_app        | INSERT
 kartova_app        | SELECT
 kartova_bypass_rls | INSERT
 kartova_bypass_rls | SELECT
(4 rows)
```

Only `INSERT` and `SELECT` are present for both roles. `UPDATE`, `DELETE`, and
`TRUNCATE` are absent — the REVOKE block executed successfully.

### 3c. RLS policy present

```
docker compose exec -T postgres psql -U postgres -d kartova -c "SELECT polname, polcmd FROM pg_policy WHERE polrelid='audit_log'::regclass;"
```

Output:

```
     polname      | polcmd 
------------------+--------
 tenant_isolation | *
(1 row)
```

`polcmd = *` means the policy applies to all commands (SELECT, INSERT, UPDATE,
DELETE). Combined with `FORCE ROW LEVEL SECURITY`, this means `kartova_app`
cannot bypass it even as a table owner.

---

## 4. Happy path — INSERT as `kartova_app`

Connection: `psql -U kartova_app -d kartova` (password `dev`, via `docker compose exec`).
GUC `app.current_tenant_id` set before insert (required by the RLS `tenant_isolation` policy).

```
docker compose exec -T postgres psql -U kartova_app -d kartova -c "
    SELECT set_config('app.current_tenant_id','00000000-0000-0000-0000-0000000000aa', false);
    INSERT INTO audit_log (id, tenant_id, seq, occurred_at, actor_type, actor_id,
      actor_display, action, target_type, target_id, data, prev_hash, row_hash)
    VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000000aa', 1, now(),
      'User', gen_random_uuid(), NULL, 'verify.insert', 'User', 'x', NULL,
      decode(repeat('00',32),'hex'), decode(repeat('11',32),'hex'));
  "
```

Output:

```
              set_config              
--------------------------------------
 00000000-0000-0000-0000-0000000000aa
(1 row)

INSERT 0 1
```

Confirms: `kartova_app` can INSERT a row when the tenant GUC is set. Row accepted
by the `tenant_isolation` WITH CHECK policy (tenant_id matches current_setting).

---

## 5. Negative path 1 — UPDATE rejected (SQLSTATE 42501)

Same role (`kartova_app`), same tenant GUC.

```
docker compose exec -T postgres psql -U kartova_app -d kartova -c "
    SELECT set_config('app.current_tenant_id','00000000-0000-0000-0000-0000000000aa', false);
    UPDATE audit_log SET action='tamper' WHERE tenant_id='00000000-0000-0000-0000-0000000000aa';
  "
```

Output:

```
              set_config              
--------------------------------------
 00000000-0000-0000-0000-0000000000aa
(1 row)

ERROR:  permission denied for table audit_log
```

Confirms: UPDATE privilege has been revoked. The DB enforces immutability — application
discipline is not the only safeguard (ADR-0018).

---

## 6. Negative path 2 — DELETE rejected (SQLSTATE 42501)

Same role (`kartova_app`), same tenant GUC.

```
docker compose exec -T postgres psql -U kartova_app -d kartova -c "
    SELECT set_config('app.current_tenant_id','00000000-0000-0000-0000-0000000000aa', false);
    DELETE FROM audit_log WHERE tenant_id='00000000-0000-0000-0000-0000000000aa';
  "
```

Output:

```
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

All scenarios verified against a live Postgres 18 container using the production
`docker/postgres/init.sql` role setup and the real `Kartova.Migrator`:

| Scenario | Result |
|----------|--------|
| Migrator applies `20260615062248_InitialAuditLog` | PASS |
| Migration recorded in `__EFMigrationsHistory` | PASS |
| `kartova_app` has only INSERT + SELECT on `audit_log` (no UPDATE/DELETE/TRUNCATE) | PASS |
| `tenant_isolation` RLS policy present (`polcmd = *`) | PASS |
| `kartova_app` INSERT with tenant GUC set | INSERT 0 1 — PASS |
| `kartova_app` UPDATE attempt | ERROR: permission denied for table audit_log (42501) — PASS |
| `kartova_app` DELETE attempt | ERROR: permission denied for table audit_log (42501) — PASS |

Branch: `feat/audit-log-foundation` · Re-verified: 2026-06-15
