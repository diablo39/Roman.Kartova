-- Exercises the AddOneSystemPerComponentIndex collapse step against a real Postgres,
-- reproducing the production table's RLS posture (ENABLE + FORCE + tenant policy).
\set ON_ERROR_STOP on

CREATE TABLE relationships (
    id           uuid PRIMARY KEY,
    tenant_id    uuid NOT NULL,
    created_at   timestamp with time zone NOT NULL,
    source_id    uuid NOT NULL,
    source_kind  character varying(64) NOT NULL,
    target_id    uuid NOT NULL,
    target_kind  character varying(64) NOT NULL,
    type         character varying(64) NOT NULL,
    origin       character varying(32) NOT NULL,
    created_by_user_id uuid NOT NULL
);

CREATE POLICY tenant_isolation ON relationships
    USING (tenant_id = current_setting('app.current_tenant_id', true)::uuid);
ALTER TABLE relationships ENABLE ROW LEVEL SECURITY;
ALTER TABLE relationships FORCE ROW LEVEL SECURITY;

-- Owner must be subject to RLS for FORCE to bite the way it does in production.
CREATE ROLE app_owner NOLOGIN;
ALTER TABLE relationships OWNER TO app_owner;
GRANT ALL ON relationships TO app_owner;
GRANT CREATE, USAGE ON SCHEMA public TO app_owner;

SET ROLE app_owner;

-- Seed as the table owner with RLS temporarily off (mirrors a migration inserting fixture data).
ALTER TABLE relationships DISABLE ROW LEVEL SECURITY;

-- Tenant A, component APP-1: three PartOf edges (the duplicate case). Oldest is SYS-OLD.
INSERT INTO relationships VALUES
 ('11111111-0000-0000-0000-000000000001','aaaaaaaa-0000-0000-0000-00000000000a','2026-07-06T10:00:00Z',
  'cccccccc-0000-0000-0000-000000000001','Application','5551d500-0000-0000-0000-0000000001d0','System','PartOf','Manual','99999999-0000-0000-0000-000000000009'),
 ('11111111-0000-0000-0000-000000000002','aaaaaaaa-0000-0000-0000-00000000000a','2026-07-07T10:00:00Z',
  'cccccccc-0000-0000-0000-000000000001','Application','5552d500-0000-0000-0000-0000000002d0','System','PartOf','Manual','99999999-0000-0000-0000-000000000009'),
 ('11111111-0000-0000-0000-000000000003','aaaaaaaa-0000-0000-0000-00000000000a','2026-07-08T10:00:00Z',
  'cccccccc-0000-0000-0000-000000000001','Application','5553d500-0000-0000-0000-0000000003d0','System','PartOf','Manual','99999999-0000-0000-0000-000000000009');

-- Same component id, DIFFERENT tenant: must survive (grouping includes tenant_id).
INSERT INTO relationships VALUES
 ('11111111-0000-0000-0000-000000000004','bbbbbbbb-0000-0000-0000-00000000000b','2026-07-06T10:00:00Z',
  'cccccccc-0000-0000-0000-000000000001','Application','5554d500-0000-0000-0000-0000000004d0','System','PartOf','Manual','99999999-0000-0000-0000-000000000009');

-- A Service with the same Guid as the Application: must survive (grouping includes source_kind).
INSERT INTO relationships VALUES
 ('11111111-0000-0000-0000-000000000005','aaaaaaaa-0000-0000-0000-00000000000a','2026-07-06T10:00:00Z',
  'cccccccc-0000-0000-0000-000000000001','Service','5555d500-0000-0000-0000-0000000005d0','System','PartOf','Manual','99999999-0000-0000-0000-000000000009');

-- A DependsOn edge from the same component: the partial predicate must leave it alone.
INSERT INTO relationships VALUES
 ('11111111-0000-0000-0000-000000000006','aaaaaaaa-0000-0000-0000-00000000000a','2026-07-06T10:00:00Z',
  'cccccccc-0000-0000-0000-000000000001','Application','6666d500-0000-0000-0000-0000000006d0','Application','DependsOn','Manual','99999999-0000-0000-0000-000000000009');

ALTER TABLE relationships ENABLE ROW LEVEL SECURITY;
ALTER TABLE relationships FORCE ROW LEVEL SECURITY;

\echo '=== CONTROL: a bare DELETE under FORCE RLS (the silent-no-op trap) ==='
DELETE FROM relationships WHERE type = 'PartOf' AND id = '11111111-0000-0000-0000-000000000003';
SELECT count(*) AS rows_still_present_after_bare_delete FROM relationships;

\echo '=== MIGRATION SQL VERBATIM FROM AddOneSystemPerComponentIndex.Up() ==='
ALTER TABLE relationships DISABLE ROW LEVEL SECURITY;

DELETE FROM relationships r
USING (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY tenant_id, source_kind, source_id
               ORDER BY created_at ASC, id ASC
           ) AS rn
    FROM relationships
    WHERE type = 'PartOf'
) ranked
WHERE r.id = ranked.id
  AND ranked.rn > 1;

ALTER TABLE relationships ENABLE ROW LEVEL SECURITY;
ALTER TABLE relationships FORCE ROW LEVEL SECURITY;

CREATE UNIQUE INDEX ux_relationships_one_system
ON relationships (tenant_id, source_kind, source_id)
WHERE type = 'PartOf';

RESET ROLE;

\echo '=== ASSERTIONS ==='
ALTER TABLE relationships DISABLE ROW LEVEL SECURITY;

SELECT 'survivor_is_oldest' AS check,
       CASE WHEN (SELECT target_id FROM relationships
                  WHERE type='PartOf' AND tenant_id='aaaaaaaa-0000-0000-0000-00000000000a'
                    AND source_kind='Application')
                 = '5551d500-0000-0000-0000-0000000001d0' THEN 'PASS' ELSE 'FAIL' END AS result;

SELECT 'one_row_per_group' AS check,
       CASE WHEN NOT EXISTS (
           SELECT 1 FROM relationships WHERE type='PartOf'
           GROUP BY tenant_id, source_kind, source_id HAVING count(*) > 1
       ) THEN 'PASS' ELSE 'FAIL' END AS result;

SELECT 'other_tenant_survived' AS check,
       CASE WHEN EXISTS (SELECT 1 FROM relationships
                         WHERE tenant_id='bbbbbbbb-0000-0000-0000-00000000000b' AND type='PartOf')
            THEN 'PASS' ELSE 'FAIL' END AS result;

SELECT 'service_kind_survived' AS check,
       CASE WHEN EXISTS (SELECT 1 FROM relationships WHERE source_kind='Service' AND type='PartOf')
            THEN 'PASS' ELSE 'FAIL' END AS result;

SELECT 'dependson_untouched' AS check,
       CASE WHEN EXISTS (SELECT 1 FROM relationships WHERE type='DependsOn')
            THEN 'PASS' ELSE 'FAIL' END AS result;

ALTER TABLE relationships ENABLE ROW LEVEL SECURITY;
ALTER TABLE relationships FORCE ROW LEVEL SECURITY;

SELECT 'rls_enabled_and_forced_after_migration' AS check,
       CASE WHEN relrowsecurity AND relforcerowsecurity THEN 'PASS' ELSE 'FAIL' END AS result
FROM pg_class WHERE relname = 'relationships';

SELECT 'index_exists' AS check,
       CASE WHEN EXISTS (SELECT 1 FROM pg_indexes WHERE indexname='ux_relationships_one_system')
            THEN 'PASS' ELSE 'FAIL' END AS result;

\echo '=== the index must now REFUSE a second PartOf edge ==='
ALTER TABLE relationships DISABLE ROW LEVEL SECURITY;
INSERT INTO relationships VALUES
 ('11111111-0000-0000-0000-00000000000f','aaaaaaaa-0000-0000-0000-00000000000a','2026-07-30T10:00:00Z',
  'cccccccc-0000-0000-0000-000000000001','Application','7777d500-0000-0000-0000-0000000007d0','System','PartOf','Manual','99999999-0000-0000-0000-000000000009');
