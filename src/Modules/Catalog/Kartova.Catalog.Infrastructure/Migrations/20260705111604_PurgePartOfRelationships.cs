using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartova.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PurgePartOfRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Retire the removed `PartOf` relationship type (superseded by `InstanceOf`; ADR-0111
            // revision 2026-07-04). `PartOf` was a shipped, creatable type, so pre-existing rows may
            // carry `type = 'PartOf'` — which no longer maps to the RelationshipType enum and would
            // throw on materialization (500) once the value is removed. Purge those rows.
            //
            // `relationships` has FORCE ROW LEVEL SECURITY, so even the table owner is subject to the
            // tenant_isolation policy; a bare DELETE would match no rows (no app.current_tenant_id set
            // during migration). Toggle RLS off for this owner-run cross-tenant purge, then restore the
            // exact prior state (ENABLE + FORCE). The tenant_isolation policy persists across the toggle.
            migrationBuilder.Sql(@"
                ALTER TABLE relationships DISABLE ROW LEVEL SECURITY;
                DELETE FROM relationships WHERE type = 'PartOf';
                ALTER TABLE relationships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE relationships FORCE ROW LEVEL SECURITY;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: purged rows referenced a relationship type that no longer exists in the
            // domain model, so there is nothing to restore.
        }
    }
}
