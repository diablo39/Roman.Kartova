import { Client } from "pg";

const ORG_A_TENANT = "11111111-1111-1111-1111-111111111111";
const CONN =
  process.env.E2E_PG_URL ??
  "postgresql://kartova_bypass_rls:dev_only@localhost:5432/kartova";

/**
 * Insert a drifted relationship row (type='PartOf' — not a current RelationshipType)
 * for OrgA, bypassing RLS. Returns a cleanup fn that deletes exactly this row.
 * Isolated so it cannot 500 other tests.
 */
export async function insertDriftEdge(sourceId: string, targetId: string): Promise<() => Promise<void>> {
  const client = new Client({ connectionString: CONN });
  await client.connect();
  const id = crypto.randomUUID();
  try {
    await client.query(
      `INSERT INTO relationships
         (id, tenant_id, source_kind, source_id, target_kind, target_id, type, origin, created_by_user_id, created_at)
       VALUES ($1, $2, 'Application', $3, 'Application', $4, 'PartOf', 'Manual', gen_random_uuid(), now())`,
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
