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

-- Wolverine envelope storage (schema "wolverine") is created lazily at API startup
-- (see src/Kartova.Migrator/Program.cs). The app role therefore needs CREATE on the DB
-- so Wolverine can create its own schema. Once Slice 3 moves Wolverine bootstrap into
-- the migrator, this grant can be tightened.
GRANT CREATE ON DATABASE kartova TO kartova_app;

-- Default privileges so objects created by migrator in schema public are DML-usable
-- by kartova_app without an explicit re-grant after every migration.
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kartova_app;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO kartova_app;
