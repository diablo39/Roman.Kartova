using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOneSystemPerComponentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // At-most-one System per component (ADR-0111 amended 2026-07-30). Partial index:
            // only PartOf edges are constrained; every other relationship type keeps its
            // unconstrained many-to-many cardinality. `type` is stored as a string
            // (EfRelationshipConfiguration.cs:50, HasConversion<string>()).
            //
            // Duplicates cannot predate 2026-07-05 (PurgePartOfRelationships deleted every PartOf
            // row), but the still-unguarded POST /catalog/relationships path can have created
            // multi-membership rows since — in any dev or deployed database. Migrations run as a
            // pre-upgrade Job (ADR-0085), so a duplicate here would fail the DEPLOY, not the app.
            // Collapse first: keep one edge per (tenant_id, source_kind, source_id) group,
            // deterministically the oldest by created_at (tie-broken by id) so the outcome is
            // reproducible across environments. A no-op when there are no duplicates.
            //
            // `relationships` has FORCE ROW LEVEL SECURITY, so a bare DELETE here would match
            // zero rows (no app.current_tenant_id set during a migration). Toggle RLS off for
            // this owner-run cross-tenant collapse, then restore the exact prior state (ENABLE +
            // FORCE) — same dance as PurgePartOfRelationships.cs:22-27. The tenant_isolation
            // policy persists across the toggle.
            //
            // Lock exposure (measured live via pg_locks): the whole Up() body below is one
            // multi-statement Sql() string inside EF's per-migration transaction, so the
            // AccessExclusiveLock that `ALTER TABLE ... DISABLE ROW LEVEL SECURITY` itself takes
            // is held continuously from that first ALTER TABLE through the CREATE UNIQUE INDEX —
            // there is no intermediate commit to release it early. Migrations run as a Helm
            // pre-upgrade Job (ADR-0085) while the *previous* release's API pods are still
            // serving traffic, so live queries touching `relationships` would otherwise queue
            // behind this lock for the whole migration. `SET LOCAL lock_timeout` makes a
            // contended deploy fail fast and retry instead of stampeding the lock queue and
            // stalling every reader behind us. `SET LOCAL` (not `SET`) is correct precisely
            // because this migration is transactional — the setting reverts at transaction end
            // either way — but if anyone later sets SuppressTransaction = true on this migration,
            // SET LOCAL silently stops applying (there is no longer a transaction to scope it to)
            // and must become a plain SET.
            //
            // Deferred alternative, so the next person does not have to rediscover it: once
            // `relationships` holds real volume, build the index with `CREATE UNIQUE INDEX
            // CONCURRENTLY` in a separate SuppressTransaction migration, followed by a
            // pg_index.indisvalid check — a concurrent build that meets a leftover duplicate
            // leaves an INVALID index behind instead of aborting, so the check is required, not
            // optional. Not done now: the table is 21 rows / 88 kB, CONCURRENTLY cannot share the
            // collapse step's transaction, and a failed concurrent build would leave the collapse
            // committed alongside an invalid index — a worse deploy failure than the one it
            // prevents.
            migrationBuilder.Sql(@"
                SET LOCAL lock_timeout = '5s';

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

                -- tenant_id is the leading index key column, not merely an included column. A
                -- unique index in Postgres is checked against the full physical index regardless
                -- of the session's RLS visibility, so a 23505 can in principle leak ""a row with
                -- this key exists in another tenant"". That does NOT happen here only because
                -- tenant_id leads: two rows can collide only if they already share a tenant.
                -- Dropping or reordering tenant_id out of the leading position — e.g.
                -- ""simplifying"" the index to rely on RLS alone — would turn constraint
                -- violations into a genuine cross-tenant existence leak.
                CREATE UNIQUE INDEX ux_relationships_one_system
                ON relationships (tenant_id, source_kind, source_id)
                WHERE type = 'PartOf';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: reverting restores the index-free state but cannot restore the
            // redundant membership rows the collapse in Up() deleted.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_relationships_one_system;");
        }
    }
}
