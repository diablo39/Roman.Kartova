import { Client } from "pg";

// These mirror .NET-side sources of truth that a TS project can't import across
// the language boundary — keep in sync:
//   ORG_A_TENANT      → SeededOrgs.OrgA / DevSeed.OrgATenantId
//   role/password     → PostgresTestBootstrap.BypassRole / BypassPassword
//                       (compose wires the same into ConnectionStrings__KartovaBypass)
const ORG_A_TENANT = "11111111-1111-1111-1111-111111111111";
const CONN =
  process.env.E2E_PG_URL ??
  "postgresql://kartova_bypass_rls:dev_only@localhost:5432/kartova";

/**
 * Insert a drifted relationship row for OrgA, bypassing RLS. Returns a cleanup fn that
 * deletes exactly this row. Isolated so it cannot 500 other tests.
 *
 * type='LegacyUnmappedType' — a string that is NOT a current Kartova.Catalog.Domain
 * RelationshipType member (src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipType.cs).
 * relationships.type is EF-persisted as a plain string (EfRelationshipConfiguration.cs:50),
 * so any string round-trips at the DB layer; genuine drift is any value the current enum
 * doesn't recognize. Do NOT use 'PartOf' here: E-03.F-03.S-01 (commit 3ebe95ba) re-added
 * PartOf as a real, visible RelationshipType for System membership, so it is no longer
 * excluded by the read-side KnownRelationshipTypes query filter (see that file's comment)
 * — a 'PartOf' row now renders as a normal relationship instead of being excluded, which
 * silently invalidated this fixture's "drift" premise (found 2026-07-31 by running
 * relationship-drift.spec.ts, not by inspection).
 */
export async function insertDriftEdge(sourceId: string, targetId: string): Promise<() => Promise<void>> {
  const client = new Client({ connectionString: CONN });
  await client.connect();
  const id = crypto.randomUUID();
  try {
    await client.query(
      `INSERT INTO relationships
         (id, tenant_id, source_kind, source_id, target_kind, target_id, type, origin, created_by_user_id, created_at)
       VALUES ($1, $2, 'Application', $3, 'Application', $4, 'LegacyUnmappedType', 'Manual', gen_random_uuid(), now())`,
      [id, ORG_A_TENANT, sourceId, targetId],
    );
  } finally {
    await client.end();
  }
  return async () => {
    const c = new Client({ connectionString: CONN });
    await c.connect();
    try {
      await c.query(`DELETE FROM relationships WHERE id = $1`, [id]);
    } finally {
      await c.end();
    }
  };
}
