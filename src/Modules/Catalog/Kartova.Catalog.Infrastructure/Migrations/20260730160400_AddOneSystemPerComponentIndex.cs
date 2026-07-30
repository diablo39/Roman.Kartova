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
            migrationBuilder.Sql(@"
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
                WHERE type = 'PartOf';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_relationships_one_system;");
        }
    }
}
