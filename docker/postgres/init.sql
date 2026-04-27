-- Roles created at cluster init time (before database creation).
-- Used by local docker-compose only. Production creates these via Helm Secrets + an init Job.

CREATE ROLE migrator WITH LOGIN PASSWORD 'dev' CREATEDB;
CREATE ROLE kartova_app WITH LOGIN PASSWORD 'dev';

-- Grant DML-only role connect rights to the default DB. The migrator role owns the schema.
GRANT CONNECT ON DATABASE kartova TO kartova_app;

-- The migrator owns the public schema so EF Core migrations can create tables.
-- (PostgreSQL 15+ revokes CREATE on public from PUBLIC by default.)
ALTER SCHEMA public OWNER TO migrator;
GRANT USAGE ON SCHEMA public TO kartova_app;

-- Wolverine Postgres persistence is deferred until a slice publishes domain events
-- (see docs/superpowers/specs/2026-04-24-defer-wolverine-persistence-design.md and ADR-0085).
-- When persistence is reintroduced, the wolverine.* schema must be created by Kartova.Migrator,
-- never at API startup, so the app role does not need CREATE on the database.

-- Default privileges so objects created by migrator in schema public are DML-usable
-- by kartova_app without an explicit re-grant after every migration.
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kartova_app;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO kartova_app;

-- ADR-0090 admin bypass path: BYPASSRLS role used exclusively by
-- AdminOrganizationDbContext for POST /api/v1/admin/organizations.
-- Enforced to that assembly by architecture tests.
CREATE ROLE kartova_bypass_rls WITH LOGIN PASSWORD 'dev_only' BYPASSRLS;
GRANT CONNECT ON DATABASE kartova TO kartova_bypass_rls;
GRANT USAGE, CREATE ON SCHEMA public TO kartova_bypass_rls;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO kartova_bypass_rls;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kartova_bypass_rls;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO kartova_bypass_rls;
